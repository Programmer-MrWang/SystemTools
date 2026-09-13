using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using SystemTools.Helpers;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.UsbDeviceTrigger", "USB设备插入时", "\uF3A3")]
public class UsbDeviceTrigger : TriggerBase<UsbDeviceTriggerConfig>
{
    private readonly DeviceNotificationWindow _notificationWindow;
    private ManagementEventWatcher? _volumeInsertWatcher;
    private readonly object _triggerSyncRoot = new();
    private readonly Dictionary<string, DateTime> _recentTriggerTime = new(StringComparer.OrdinalIgnoreCase);

    public UsbDeviceTrigger()
    {
        _notificationWindow = new DeviceNotificationWindow();
        _notificationWindow.DeviceArrived += OnDeviceArrived;
    }

    public override void Loaded()
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        UpdateDetectionMode();
    }

    public override void UnLoaded()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        StopVolumeWatcher();
        _notificationWindow.Unregister();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsbDeviceTriggerConfig.OnlyUsbStorage))
        {
            UpdateDetectionMode();
        }
    }

    private void UpdateDetectionMode()
    {
        StopVolumeWatcher();
        _notificationWindow.Unregister();

        if (Settings.OnlyUsbStorage)
        {
            try
            {
                StartVolumeWatcher();
            }
            catch
            {
                // WMI 在少数系统环境中可能不可用，回退到设备广播监听。
                _notificationWindow.Register();
            }
        }
        else
        {
            _notificationWindow.Register();
        }
    }

    private void StartVolumeWatcher()
    {
        // EventType = 2 => 配置变更事件里的“设备到达（卷插入）”。
        const string query = "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2";
        _volumeInsertWatcher = new ManagementEventWatcher(new WqlEventQuery(query));
        _volumeInsertWatcher.EventArrived += OnVolumeInserted;
        _volumeInsertWatcher.Start();
    }

    private void StopVolumeWatcher()
    {
        if (_volumeInsertWatcher == null)
        {
            return;
        }

        _volumeInsertWatcher.EventArrived -= OnVolumeInserted;
        try
        {
            _volumeInsertWatcher.Stop();
        }
        catch
        {
            // 忽略停止过程中的异常，保证触发器卸载流程稳定。
        }

        _volumeInsertWatcher.Dispose();
        _volumeInsertWatcher = null;
    }

    private async void OnVolumeInserted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var driveName = e.NewEvent.Properties["DriveName"]?.Value?.ToString();
            if (string.IsNullOrWhiteSpace(driveName))
            {
                return;
            }

            if (!TryNormalizeDriveRoot(driveName, out var driveRoot))
            {
                return;
            }

            // 判定与就绪等待涉及 WMI/IO，放到线程池执行，避免占用 WMI 回调线程。
            var key = await Task.Run(() => ResolveUsbVolumeKeyAsync(driveRoot)).ConfigureAwait(false);
            if (key is null)
            {
                return;
            }

            if (!TryMarkTriggered(key))
            {
                return;
            }

            Trigger();
        }
        catch
        {
            // 单次卷事件处理失败不应影响后续事件。
        }
    }

    /// <summary>
    /// 判定卷是否为 U 盘（USB 存储），并返回用于去重的键；不满足条件时返回 <c>null</c>。
    /// </summary>
    private static async Task<string?> ResolveUsbVolumeKeyAsync(string driveRoot)
    {
        // Windows 常把 U 盘识别为固定磁盘，需按物理磁盘总线类型判定，不能只看 DriveType。
        var info = UsbStorageUtils.Classify(driveRoot);

        // 卷刚插入时可能尚未挂载完成，等待就绪后再交由自动化处理。
        if (!await UsbStorageUtils.WaitUntilReadyAsync(driveRoot).ConfigureAwait(false))
        {
            return null;
        }

        // USB 总线上的卷，或传统可移动介质卷，都视为“U 盘”类设备。
        if (!info.IsUsbStorage && !UsbStorageUtils.IsRemovableVolume(driveRoot))
        {
            return null;
        }

        // 同一物理磁盘的多个分区（如多分区启动盘）只应触发一次。
        return info.DiskNumber is uint diskNumber ? $"usb-disk:{diskNumber}" : $"volume:{driveRoot}";
    }

    private void OnDeviceArrived(object? sender, EventArgs e)
    {
        if (!TryMarkTriggered("device-arrived"))
        {
            return;
        }

        Trigger();
    }

    private bool TryMarkTriggered(string key)
    {
        var now = DateTime.Now;

        lock (_triggerSyncRoot)
        {
            if (now - Settings.LastTriggered < TimeSpan.FromSeconds(1))
            {
                return false;
            }

            if (_recentTriggerTime.TryGetValue(key, out var last) &&
                now - last < TimeSpan.FromSeconds(3))
            {
                return false;
            }

            Settings.LastTriggered = now;
            _recentTriggerTime[key] = now;
            return true;
        }
    }

    private static bool TryNormalizeDriveRoot(string driveName, out string driveRoot)
    {
        driveRoot = string.Empty;

        try
        {
            driveRoot = Path.GetPathRoot(driveName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(driveRoot))
            {
                return false;
            }

            if (!driveRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                driveRoot += Path.DirectorySeparatorChar;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private class DeviceNotificationWindow : NativeWindow
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVTYP_DEVICEINTERFACE = 0x05;

        private static readonly Guid GuidDevinterfaceUSBDevice = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

        private IntPtr _notificationHandle;

        public event EventHandler? DeviceArrived;

        public void Register()
        {
            if (Handle != IntPtr.Zero)
            {
                return;
            }

            CreateHandle(new CreateParams());

            var dbi = new DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
                dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_classguid = GuidDevinterfaceUSBDevice
            };

            var ptr = Marshal.AllocHGlobal(dbi.dbcc_size);
            Marshal.StructureToPtr(dbi, ptr, false);

            _notificationHandle = RegisterDeviceNotification(Handle, ptr, 0);
            Marshal.FreeHGlobal(ptr);

            if (_notificationHandle == IntPtr.Zero)
                throw new Exception($"注册设备通知失败，错误码: {Marshal.GetLastWin32Error()}");
        }

        public void Unregister()
        {
            if (_notificationHandle != IntPtr.Zero)
            {
                UnregisterDeviceNotification(_notificationHandle);
                _notificationHandle = IntPtr.Zero;
            }

            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE && (int)m.WParam == DBT_DEVICEARRIVAL)
            {
                var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(m.LParam);
                if (hdr.dbch_devicetype == DBT_DEVTYP_DEVICEINTERFACE)
                {
                    DeviceArrived?.Invoke(this, EventArgs.Empty);
                }
            }

            base.WndProc(ref m);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterDeviceNotification(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct DEV_BROADCAST_HDR
        {
            public int dbch_size;
            public int dbch_devicetype;
            public int dbch_reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            public char dbcc_name;
        }
    }
}

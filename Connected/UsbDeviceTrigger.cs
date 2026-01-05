using System;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.UsbDeviceTrigger", "USB设备插入时", "\uF3A3")]
public class UsbDeviceTrigger : TriggerBase<UsbDeviceTriggerConfig>
{
    private readonly DeviceNotificationWindow _notificationWindow;

    public UsbDeviceTrigger()
    {
        _notificationWindow = new DeviceNotificationWindow();
        _notificationWindow.DeviceArrived += OnDeviceArrived;
    }

    public override void Loaded()
    {
        _notificationWindow.Register();
    }

    public override void UnLoaded()
    {
        _notificationWindow.Unregister();
    }

    private void OnDeviceArrived(object? sender, EventArgs e)
    {
        if (DateTime.Now - Settings.LastTriggered < TimeSpan.FromSeconds(1))
            return;

        Settings.LastTriggered = DateTime.Now;
        Trigger();
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

            DestroyHandle();
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
        private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, uint flags);

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
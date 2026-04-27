using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using SystemTools.Shared;

namespace SystemTools.Services;

public class UsbAutoPlayService(ILogger<UsbAutoPlayService> logger)
{
    private readonly ILogger<UsbAutoPlayService> _logger = logger;
    private ManagementEventWatcher? _volumeInsertWatcher;

    public void Start()
    {
        ApplyConfig();
    }

    public void Stop()
    {
        StopWatcher();
    }

    public void ApplyConfig()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            StopWatcher();
            return;
        }

        if (GlobalConstants.MainConfig?.Data.AutoOpenUsbDriveOnInsert != true)
        {
            StopWatcher();
            return;
        }

        if (_volumeInsertWatcher != null)
        {
            return;
        }

        try
        {
            const string query = "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2";
            _volumeInsertWatcher = new ManagementEventWatcher(new WqlEventQuery(query));
            _volumeInsertWatcher.EventArrived += OnVolumeInserted;
            _volumeInsertWatcher.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动播放服务启动失败。");
            StopWatcher();
        }
    }

    private void OnVolumeInserted(object sender, EventArrivedEventArgs e)
    {
        var driveName = e.NewEvent.Properties["DriveName"]?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(driveName))
        {
            return;
        }

        var driveRoot = Path.GetPathRoot(driveName) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return;
        }

        try
        {
            var driveInfo = new DriveInfo(driveRoot);
            if (!driveInfo.IsReady || driveInfo.DriveType != DriveType.Removable)
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = driveRoot,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "自动打开U盘失败：{DriveRoot}", driveRoot);
        }
    }

    private void StopWatcher()
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
            // Ignore stop errors.
        }

        _volumeInsertWatcher.Dispose();
        _volumeInsertWatcher = null;
    }
}

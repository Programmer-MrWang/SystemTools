using System;
using System.Collections.Generic;
using System.Management;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Windows;

public sealed class WindowsDeviceCatalog : IDeviceCatalog
{
    public IReadOnlyList<DeviceOption> GetDevices()
    {
        var options = new List<DeviceOption>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID IS NOT NULL");
            using var devices = searcher.Get();
            foreach (ManagementObject device in devices)
            {
                using (device)
                {
                    var id = device["PNPDeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var name = device["Name"]?.ToString();
                    options.Add(new(string.IsNullOrWhiteSpace(name) ? id : $"{name} ({id})", id));
                }
            }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return options;
    }
}

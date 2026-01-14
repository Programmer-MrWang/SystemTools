using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SystemTools.Utils;

public static class DriveUtils
{
    private static string GetDriveJsonPath()
    {
        var pluginDir = Path.GetDirectoryName(typeof(DriveUtils).Assembly.Location);
        return Path.Combine(pluginDir, "drive.json");
    }

    public static List<string> GetCurrentDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.Name.TrimEnd('\\'))
            .ToList();
    }

    public static List<string> LoadSavedDrives()
    {
        var path = GetDriveJsonPath();
        if (!File.Exists(path))
            return new List<string>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static void SaveDrives(List<string> drives)
    {
        var path = GetDriveJsonPath();
        var json = JsonSerializer.Serialize(drives);
        File.WriteAllText(path, json);
    }

    public static void InitializeDriveRecord()
    {
        var currentDrives = GetCurrentDrives();
        SaveDrives(currentDrives);
    }

    public static List<string> GetNewDrives(List<string> previousDrives)
    {
        var currentDrives = GetCurrentDrives();
        return currentDrives.Except(previousDrives).ToList();
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassIsland.Shared.Helpers;
using SystemTools.Shared;

namespace SystemTools.ConfigHandlers;

/// <summary>
/// 管理悬浮窗配置方案的存储和加载，每个方案为独立的 JSON 文件。
/// </summary>
public class FloatingWindowProfileManager
{
    private readonly string _profilesDirectory;
    private FloatingWindowProfile _currentProfile = new();
    private string _currentProfileName = "Default";

    public static FloatingWindowProfile DefaultProfile { get; } = new()
    {
        Name = "Default",
        FloatingWindowScale = 1.0,
        FloatingWindowIconSize = 22,
        FloatingWindowTextSize = 12,
        FloatingWindowOpacity = 80,
        FloatingWindowPositionX = 100,
        FloatingWindowPositionY = 100,
        FloatingWindowLayer = 1,
        FloatingWindowLayerRecheckMode = 1,
        FloatingWindowShadowEnabled = true,
        FloatingWindowButtonOrder = new List<string>(),
        FloatingWindowButtonRows = new List<List<string>>(),
        FloatingWindowButtonRulesets = new Dictionary<string, ButtonRulesetConfig>(),
        FloatingWindowRowRulesets = new List<RowRulesetConfig>()
    };

    public FloatingWindowProfileManager()
    {
        _profilesDirectory = Path.Combine(DependencyPaths.GetDependencyRoot(), "FloatingWindowProfiles");
        if (!Directory.Exists(_profilesDirectory))
        {
            Directory.CreateDirectory(_profilesDirectory);
        }
    }

    /// <summary>
    /// 从旧版 MainConfigData 迁移配置到文件存储
    /// </summary>
    public void MigrateFromLegacyConfig(MainConfigData legacyData)
    {
        var defaultPath = GetProfilePath("Default");
        if (File.Exists(defaultPath))
        {
            return;
        }

        var profile = new FloatingWindowProfile
        {
            Name = "Default",
            FloatingWindowHorizontal = legacyData.FloatingWindowHorizontal,
            FloatingWindowButtonOrder = new List<string>(legacyData.FloatingWindowButtonOrder ?? []),
            FloatingWindowButtonRows = (legacyData.FloatingWindowButtonRows ?? []).Select(r => new List<string>(r)).ToList(),
            FloatingWindowScale = legacyData.FloatingWindowScale,
            FloatingWindowIconSize = legacyData.FloatingWindowIconSize,
            FloatingWindowTextSize = legacyData.FloatingWindowTextSize,
            FloatingWindowOpacity = legacyData.FloatingWindowOpacity,
            FloatingWindowPositionX = legacyData.FloatingWindowPositionX,
            FloatingWindowPositionY = legacyData.FloatingWindowPositionY,
            FloatingWindowLayer = legacyData.FloatingWindowLayer,
            FloatingWindowLayerRecheckMode = legacyData.FloatingWindowLayerRecheckMode,
            FloatingWindowShadowEnabled = legacyData.FloatingWindowShadowEnabled,
            FloatingWindowDragHandleAlwaysVisible = legacyData.FloatingWindowDragHandleAlwaysVisible,
            FloatingWindowButtonRulesets = new Dictionary<string, ButtonRulesetConfig>(legacyData.FloatingWindowButtonRulesets ?? []),
            FloatingWindowRowRulesets = new List<RowRulesetConfig>(legacyData.FloatingWindowRowRulesets ?? [])
        };

        ConfigureFileHelper.SaveConfig(defaultPath, profile);
    }

    public string ProfilesDirectory => _profilesDirectory;

    /// <summary>
    /// 判断指定名称的方案文件是否存在于磁盘。
    /// </summary>
    public bool ProfileFileExists(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        return File.Exists(GetProfilePath(profileName));
    }

    public FloatingWindowProfile CurrentProfile => _currentProfile;

    public string CurrentProfileName
    {
        get => _currentProfileName;
        private set
        {
            if (_currentProfileName == value) return;
            _currentProfileName = value;
            CurrentProfile.Name = value;
        }
    }

    /// <summary>
    /// 获取所有可用的方案名称列表
    /// </summary>
    public IReadOnlyList<string> GetProfileNames()
    {
        if (!Directory.Exists(_profilesDirectory))
        {
            return new List<string> { "Default" };
        }

        var names = Directory.GetFiles(_profilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();

        if (names.Count == 0)
        {
            names.Add("Default");
        }

        return names;
    }

    /// <summary>
    /// 加载指定名称的方案
    /// </summary>
    public void LoadProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName = "Default";
        }

        var path = GetProfilePath(profileName);
        if (!File.Exists(path))
        {
            // 文件不存在时只在内存中加载默认模板，不写回磁盘，
            // 避免被显式删除的方案被自动重建。
            _currentProfile = ConfigureFileHelper.CopyObject(DefaultProfile);
            _currentProfile.Name = profileName;
        }
        else
        {
            _currentProfile = ConfigureFileHelper.LoadConfig<FloatingWindowProfile>(path);
            _currentProfile.Name = profileName;
        }

        _currentProfileName = profileName;
    }

    /// <summary>
    /// 保存当前方案
    /// </summary>
    public void SaveProfile()
    {
        var path = GetProfilePath(_currentProfileName);
        ConfigureFileHelper.SaveConfig(path, _currentProfile);
    }

    /// <summary>
    /// 创建新方案，基于当前方案或默认方案
    /// </summary>
    public string CreateProfile(string? name = null)
    {
        var baseName = name?.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"Profile {GetProfileNames().Count + 1}";
        }

        var profileName = baseName;
        var counter = 1;
        while (File.Exists(GetProfilePath(profileName)))
        {
            profileName = $"{baseName} ({counter})";
            counter++;
        }

        var newProfile = ConfigureFileHelper.CopyObject(_currentProfile);
        newProfile.Name = profileName;

        var path = GetProfilePath(profileName);
        ConfigureFileHelper.SaveConfig(path, newProfile);

        return profileName;
    }

    /// <summary>
    /// 删除指定方案
    /// </summary>
    public bool RemoveProfile(string profileName)
    {
        if (string.Equals(profileName, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = GetProfilePath(profileName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 重命名方案
    /// </summary>
    public bool RenameProfile(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var oldPath = GetProfilePath(oldName);
        var newPath = GetProfilePath(newName);

        if (!File.Exists(oldPath) || File.Exists(newPath))
        {
            return false;
        }

        try
        {
            File.Move(oldPath, newPath);
            if (string.Equals(_currentProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _currentProfileName = newName;
                _currentProfile.Name = newName;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetProfilePath(string profileName)
    {
        return Path.Combine(_profilesDirectory, $"{profileName}.json");
    }
}

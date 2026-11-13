using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemTools.ConfigHandlers;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.Shared;
using SystemTools.Services;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;

namespace SystemTools;

public enum FeatureItemType
{
    Action,
    Trigger,
    Component,
    Rule
}

public sealed record SpeechRecognitionDownloadOption(
    string ModelName,
    string DisplayName,
    string Url,
    string ExpectedMd5);

public partial class UnifiedFeatureItem : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private FeatureItemType _itemType;
    [ObservableProperty] private string? _groupName;

    public string TypeDisplayName => ItemType switch
    {
        FeatureItemType.Action => "行动",
        FeatureItemType.Trigger => "触发器",
        FeatureItemType.Component => "组件",
        FeatureItemType.Rule => "规则",
        _ => "未知"
    };
}

public partial class FloatingTriggerItem : ObservableObject
{
    [ObservableProperty] private string _buttonId = string.Empty;
    [ObservableProperty] private string _icon = string.Empty;
    [ObservableProperty] private string _buttonName = string.Empty;
    [ObservableProperty] private bool _isRulesetExpanded = false;
    [ObservableProperty] private ButtonRulesetConfig _config = new();

    /// <summary>
    /// 图标表达式解析出的图标源，供 IconSourceElement 显示。兼容旧版 /uXXXX 配置。
    /// </summary>
    public FluentAvalonia.UI.Controls.FAIconSource? IconSource =>
        SystemTools.Services.FloatingWindowIconProvider.TryResolveIconSource(Icon);

    partial void OnIconChanged(string value) { OnPropertyChanged(nameof(IconSource)); }
}

public partial class FloatingTriggerRow : ObservableObject
{
    [ObservableProperty] private ObservableCollection<FloatingTriggerItem> _buttons = new();
    [ObservableProperty] private int _rowIndex = 0;
    [ObservableProperty] private RowRulesetConfig _rowRuleset = new();
    [ObservableProperty] private bool _isRulesetExpanded = false;
}

public partial class SystemToolsSettingsViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private MainConfigData _settings;

    [ObservableProperty] private bool _isFfmpegDownloadEnabled = true;
    [ObservableProperty] private bool _isFaceModelsDownloadEnabled = true;
    [ObservableProperty] private bool _isVoskWorkerDownloadEnabled = true;
    [ObservableProperty] private bool _isMoreFeaturesClickEnabled = true;
    [ObservableProperty] private bool _isDownloadInProgress;

    [ObservableProperty] private SpeechRecognitionDownloadOption? _selectedSpeechRecognitionModel;
    [ObservableProperty] private string _speechRecognitionActionText = "下载";
    [ObservableProperty] private bool _isSpeechRecognitionActionEnabled;
    [ObservableProperty] private bool _isSpeechRecognitionModelSelectionEnabled = true;

    [ObservableProperty] private bool _showDownloadProgress = false;
    [ObservableProperty] private double _downloadProgress = 0;
    [ObservableProperty] private string _downloadStatusText = string.Empty;

    private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);

    [ObservableProperty] private ObservableCollection<UnifiedFeatureItem> _featureItems = new();
    [ObservableProperty] private ObservableCollection<UnifiedFeatureItem> _featureSearchResults = new();

    public bool IsFeatureSearchEmpty => FeatureSearchResults.Count == 0;

    // Drawer 
    [ObservableProperty] private bool _isFeatureDrawerOpen = false;
    [ObservableProperty] private object? _featureDrawerContent;

    private readonly MainConfigHandler _configHandler;
    private readonly FloatingWindowService _floatingWindowService;
    private readonly EventHandler _entriesChangedHandler;

    [ObservableProperty] private ObservableCollection<FloatingTriggerRow> _floatingTriggerRows = new();
    [ObservableProperty] private bool _hasFloatingTriggerEntries;

    // 选中状态
    [ObservableProperty] private FloatingTriggerRow? _selectedFloatingTriggerRow;
    [ObservableProperty] private FloatingTriggerItem? _selectedFloatingTriggerItem;

    // 悬浮窗配置方案
    [ObservableProperty] private ObservableCollection<string> _floatingWindowProfileNames = new();
    [ObservableProperty] private string _selectedFloatingWindowProfile = "Default";

    public static IReadOnlyList<SpeechRecognitionDownloadOption> SpeechRecognitionModels { get; } =
    [
        new("SenseVoiceSmall ONNX (INT8 Quantized)", "SenseVoiceSmall ONNX (INT8 Quantized)（211 MB）",
            "https://livefile.xesimg.com/programme/python_assets/7bd22f71831a0023a2e2673773235878.zip",
            "7bd22f71831a0023a2e2673773235878"),
        new("vosk-model-small-en-us", "vosk-model-small-en-us（40 MB）",
            "https://livefile.xesimg.com/programme/python_assets/a357a5217db6fd0cbc22bcf173253350.zip",
            "a357a5217db6fd0cbc22bcf173253350"),
        new("vosk-model-small-cn", "vosk-model-small-cn（42 MB）",
            "https://livefile.xesimg.com/programme/python_assets/79dae7671c95fd26f1436ef2f5ec1fa0.zip",
            "79dae7671c95fd26f1436ef2f5ec1fa0"),
        new("vosk-model-en-us", "vosk-model-en-us（1.78 GB）",
            "https://livefile.xesimg.com/programme/python_assets/4e57edf6e390022ea30cbf408a1fafbb.zip",
            "4e57edf6e390022ea30cbf408a1fafbb"),
        new("vosk-model-cn", "vosk-model-cn（1.26 GB）",
            "https://livefile.xesimg.com/programme/python_assets/ceb27377d45f168ae08d218daf15cace.zip",
            "ceb27377d45f168ae08d218daf15cace")
    ];

    private const string DownloadUrl =
        "https://livefile.xesimg.com/programme/python_assets/f94fcfa40c9de41d6df09566a51e3130.exe";
    private const string ExpectedMd5 = "f94fcfa40c9de41d6df09566a51e3130";
    private const string TempFileName = "f94fcfa40c9de41d6df09566a51e3130.exe";
    private const string TargetFileName = "ffmpeg.exe";

    private const string FaceModelsUrl = "https://livefile.xesimg.com/programme/python_assets/915f822a03487c4e5761b4fcf8f206cc.zip";
    private const string FaceModelsMd5 = "915f822a03487c4e5761b4fcf8f206cc";
    private const string FaceZipFileName = "FaceModels.zip";

    private const string VoskWorkerUrl =
        "https://livefile.xesimg.com/programme/python_assets/5f382436a14e07bc59186b61b02735a0.zip";
    private const string VoskWorkerMd5 = "5f382436a14e07bc59186b61b02735a0";
    private const string VoskWorkerZipFileName = "VoskWorker.zip";

    public SystemToolsSettingsViewModel(MainConfigHandler configHandler, FloatingWindowService floatingWindowService)
    {
        _configHandler = configHandler;
        _floatingWindowService = floatingWindowService;
        _settings = configHandler.Data;
        _entriesChangedHandler = (_, _) => Dispatcher.UIThread.Post(RefreshFloatingTriggers);
        _floatingWindowService.EntriesChanged += _entriesChangedHandler;
        _selectedSpeechRecognitionModel = SpeechRecognitionModels[0];
    }

    public void InitializeFeatureItems()
    {
        FeatureItems.Clear();

        foreach (var status in GlobalConstants.Features!.Snapshot())
        {
            var feature = status.Feature;
            if (feature.Kind == "data" || feature.Id is "SystemTools.FullscreenClock" or "SystemTools.ClickSimulation") continue;
            var type = feature.Kind switch { "component" => FeatureItemType.Component, "trigger" => FeatureItemType.Trigger, "rule" => FeatureItemType.Rule, _ => FeatureItemType.Action };
            FeatureItems.Add(new UnifiedFeatureItem
            {
                Id = feature.Id, DisplayName = feature.Name, ItemType = type, GroupName = feature.Group,
                IsEnabled = type switch { FeatureItemType.Component => Settings.IsComponentEnabled(feature.Id), FeatureItemType.Trigger => Settings.IsTriggerEnabled(feature.Id), FeatureItemType.Rule => Settings.IsRuleEnabled(feature.Id), _ => Settings.IsActionEnabled(feature.Id) }
            });
        }
        UpdateFeatureSearchResults(null);
    }

    public void UpdateFeatureSearchResults(string? searchText)
    {
        var keyword = searchText?.Trim();
        FeatureSearchResults.Clear();

        foreach (var item in FeatureItems.Where(item => MatchesFeatureSearch(item, keyword)))
        {
            FeatureSearchResults.Add(item);
        }

        OnPropertyChanged(nameof(IsFeatureSearchEmpty));
    }

    private static bool MatchesFeatureSearch(UnifiedFeatureItem item, string? keyword)
    {
        return string.IsNullOrEmpty(keyword) ||
               item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.TypeDisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               item.GroupName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true ||
               item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    public void SaveFeatureSettings()
    {
        foreach (var item in FeatureItems)
        {
            switch (item.ItemType)
            {
                case FeatureItemType.Action:
                    Settings.EnabledActions[item.Id] = item.IsEnabled;
                    break;
                case FeatureItemType.Trigger:
                    Settings.EnabledTriggers[item.Id] = item.IsEnabled;
                    break;
                case FeatureItemType.Component:
                    Settings.EnabledComponents[item.Id] = item.IsEnabled;
                    break;
                case FeatureItemType.Rule:
                    Settings.EnabledRules[item.Id] = item.IsEnabled;
                    break;
            }
        }

        _configHandler.Save();
    }

    public FloatingWindowProfile CurrentFloatingWindowProfile => _floatingWindowService.ProfileManager.CurrentProfile;

    /// <summary>
    /// 悬浮窗方案 JSON 文件所在目录，供 UI 层打开文件夹/重名检测使用。
    /// </summary>
    public string FloatingWindowProfilesDirectory => _floatingWindowService.ProfileManager.ProfilesDirectory;

    public void RefreshFloatingWindowProfiles()
    {
        var names = _floatingWindowService.ProfileManager.GetProfileNames();
        FloatingWindowProfileNames.Clear();
        foreach (var name in names)
        {
            FloatingWindowProfileNames.Add(name);
        }
        SelectedFloatingWindowProfile = _floatingWindowService.ProfileManager.CurrentProfileName;
    }

    public void RefreshFloatingTriggers()
    {
        _floatingWindowService.EnsureUniqueButtonIds();
        var entries = _floatingWindowService.Entries
            .GroupBy(x => x.ButtonId)
            .ToDictionary(x => x.Key, x => x.First());
        HasFloatingTriggerEntries = entries.Count > 0;

        var profile = CurrentFloatingWindowProfile;
        // 收集已配置的按钮ID
        var configuredIds = new HashSet<string>();
        foreach (var row in profile.FloatingWindowButtonRows ?? [])
        {
            foreach (var id in row)
            {
                configuredIds.Add(id);
            }
        }

        // 如果没有任何按钮被配置到行中，自动将所有可用按钮添加到第一行
        // 这样用户首次使用或从旧版本迁移时，按钮默认会显示出来
        if (configuredIds.Count == 0 && entries.Count > 0)
        {
            var allButtonIds = entries.Values.Select(e => e.ButtonId).ToList();
            if (profile.FloatingWindowButtonRows == null || profile.FloatingWindowButtonRows.Count == 0)
            {
                profile.FloatingWindowButtonRows = [allButtonIds];
            }
            else
            {
                profile.FloatingWindowButtonRows[0] = allButtonIds;
            }
            foreach (var id in allButtonIds)
            {
                configuredIds.Add(id);
            }
            _floatingWindowService.ProfileManager.SaveProfile();
        }

        // 新注册且尚未配置的按钮自动追加到第一行
        // 已存在按钮配置（如被用户移除/隐藏）的按钮不再自动添加
        var newButtonIds = entries.Values
            .Where(e => !configuredIds.Contains(e.ButtonId))
            .Where(e => !profile.FloatingWindowButtonRulesets.ContainsKey(e.ButtonId))
            .Select(e => e.ButtonId)
            .ToList();
        if (newButtonIds.Count > 0)
        {
            if (profile.FloatingWindowButtonRows == null || profile.FloatingWindowButtonRows.Count == 0)
            {
                profile.FloatingWindowButtonRows = [newButtonIds];
            }
            else
            {
                profile.FloatingWindowButtonRows[0] = [.. profile.FloatingWindowButtonRows[0], .. newButtonIds];
            }
            foreach (var id in newButtonIds)
            {
                configuredIds.Add(id);
            }
            _floatingWindowService.ProfileManager.SaveProfile();
        }

        // 注销旧对象上的事件处理程序，避免重复注册和内存泄漏
        foreach (var oldRow in FloatingTriggerRows)
        {
            oldRow.RowRuleset.PropertyChanged -= OnRowRulesetPropertyChanged;
            if (oldRow.RowRuleset.HidingRules is INotifyPropertyChanged oldRowHidingRules)
            {
                oldRowHidingRules.PropertyChanged -= OnRowRulesetPropertyChanged;
            }
            foreach (var oldItem in oldRow.Buttons)
            {
                oldItem.Config.PropertyChanged -= OnButtonConfigPropertyChanged;
                if (oldItem.Config.HidingRules is INotifyPropertyChanged oldBtnHidingRules)
                {
                    oldBtnHidingRules.PropertyChanged -= OnButtonConfigPropertyChanged;
                }
            }
        }

        // 构建已配置的行显示
        FloatingTriggerRows.Clear();
        var rowConfigs = profile.FloatingWindowRowRulesets;
        var rowIndex = 0;
        var needSave = false;
        foreach (var row in profile.FloatingWindowButtonRows ?? [])
        {
            while (rowConfigs.Count <= rowIndex)
            {
                rowConfigs.Add(new RowRulesetConfig());
                needSave = true;
            }
            var vmRow = new FloatingTriggerRow
            {
                RowIndex = rowIndex + 1,
                RowRuleset = rowConfigs[rowIndex]
            };
            vmRow.RowRuleset.PropertyChanged += OnRowRulesetPropertyChanged;
            if (vmRow.RowRuleset.HidingRules is INotifyPropertyChanged rowHidingRules)
            {
                rowHidingRules.PropertyChanged += OnRowRulesetPropertyChanged;
            }
            foreach (var id in row)
            {
                if (!entries.TryGetValue(id, out var entry))
                {
                    continue;
                }
                if (!profile.FloatingWindowButtonRulesets.TryGetValue(entry.ButtonId, out var btnConfig))
                {
                    btnConfig = new ButtonRulesetConfig();
                    profile.FloatingWindowButtonRulesets[entry.ButtonId] = btnConfig;
                    needSave = true;
                }
                var item = new FloatingTriggerItem
                {
                    ButtonId = entry.ButtonId,
                    Icon = entry.Icon,
                    ButtonName = entry.LayoutName,
                    Config = btnConfig
                };
                item.Config.PropertyChanged += OnButtonConfigPropertyChanged;
                if (item.Config.HidingRules is INotifyPropertyChanged btnHidingRules)
                {
                    btnHidingRules.PropertyChanged += OnButtonConfigPropertyChanged;
                }
                vmRow.Buttons.Add(item);
            }
            FloatingTriggerRows.Add(vmRow);
            rowIndex++;
        }

        if (FloatingTriggerRows.Count == 0)
        {
            if (rowConfigs.Count == 0)
            {
                rowConfigs.Add(new RowRulesetConfig());
                needSave = true;
            }
            var emptyRow = new FloatingTriggerRow
            {
                RowIndex = 1,
                RowRuleset = rowConfigs[0]
            };
            emptyRow.RowRuleset.PropertyChanged += OnRowRulesetPropertyChanged;
            if (emptyRow.RowRuleset.HidingRules is INotifyPropertyChanged emptyRowHidingRules)
            {
                emptyRowHidingRules.PropertyChanged += OnRowRulesetPropertyChanged;
            }
            FloatingTriggerRows.Add(emptyRow);
        }

        // 如果有新创建的默认配置，确保保存
        if (needSave)
        {
            _floatingWindowService.ProfileManager.SaveProfile();
        }

    }

    private void OnButtonConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 规则集求值时会写入 State（Ruleset/RuleGroup/Rule），避免因此递归触发通知
        if (IsRulesetStateProperty(e.PropertyName))
        {
            return;
        }

        _floatingWindowService.ProfileManager.SaveProfile();
        _floatingWindowService.UpdateWindowState();
        IAppHost.TryGetService<IRulesetService>()?.NotifyStatusChanged();
    }

    private void OnRowRulesetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 规则集求值时会写入 State（Ruleset/RuleGroup/Rule），避免因此递归触发通知
        if (IsRulesetStateProperty(e.PropertyName))
        {
            return;
        }

        _floatingWindowService.ProfileManager.SaveProfile();
        _floatingWindowService.UpdateWindowState();
        IAppHost.TryGetService<IRulesetService>()?.NotifyStatusChanged();
    }

    private static bool IsRulesetStateProperty(string? propertyName)
    {
        return propertyName == nameof(ClassIsland.Core.Models.Ruleset.Ruleset.State)
            || propertyName == nameof(ClassIsland.Core.Models.Ruleset.RuleGroup.State)
            || propertyName == nameof(ClassIsland.Core.Models.Ruleset.Rule.State);
    }

    public void AddFloatingTriggerRow()
    {
        var profile = CurrentFloatingWindowProfile;
        var rowRulesets = profile.FloatingWindowRowRulesets;
        var newRowRuleset = new RowRulesetConfig();
        rowRulesets.Add(newRowRuleset);
        var newRow = new FloatingTriggerRow
        {
            RowIndex = FloatingTriggerRows.Count + 1,
            RowRuleset = newRowRuleset
        };
        newRow.RowRuleset.PropertyChanged += OnRowRulesetPropertyChanged;
        if (newRow.RowRuleset.HidingRules is INotifyPropertyChanged rowHidingRules)
        {
            rowHidingRules.PropertyChanged += OnRowRulesetPropertyChanged;
        }
        FloatingTriggerRows.Add(newRow);
        PersistFloatingTriggerRows();
    }

    public void InsertFloatingTriggerRow(int insertIndex)
    {
        var profile = CurrentFloatingWindowProfile;
        var rowRulesets = profile.FloatingWindowRowRulesets;
        insertIndex = Math.Clamp(insertIndex, 0, FloatingTriggerRows.Count);
        var newRowRuleset = new RowRulesetConfig();
        rowRulesets.Insert(insertIndex, newRowRuleset);
        var newRow = new FloatingTriggerRow
        {
            RowIndex = insertIndex + 1,
            RowRuleset = newRowRuleset
        };
        newRow.RowRuleset.PropertyChanged += OnRowRulesetPropertyChanged;
        if (newRow.RowRuleset.HidingRules is INotifyPropertyChanged rowHidingRules)
        {
            rowHidingRules.PropertyChanged += OnRowRulesetPropertyChanged;
        }
        FloatingTriggerRows.Insert(insertIndex, newRow);

        // 重新计算后续行的索引
        for (int i = insertIndex; i < FloatingTriggerRows.Count; i++)
        {
            FloatingTriggerRows[i].RowIndex = i + 1;
        }

        PersistFloatingTriggerRows();
    }

    public bool RemoveFloatingTriggerRow(FloatingTriggerRow row)
    {
        var index = FloatingTriggerRows.IndexOf(row);
        if (index < 0 || FloatingTriggerRows.Count <= 1)
        {
            return false;
        }

        // 注销被移除行的事件处理程序
        row.RowRuleset.PropertyChanged -= OnRowRulesetPropertyChanged;
        if (row.RowRuleset.HidingRules is INotifyPropertyChanged rowHidingRules)
        {
            rowHidingRules.PropertyChanged -= OnRowRulesetPropertyChanged;
        }

        var targetRow = index > 0 ? FloatingTriggerRows[index - 1] : FloatingTriggerRows[index + 1];
        foreach (var item in row.Buttons)
        {
            // 按钮的 Config 事件监听保持不变（对象引用不变，事件仍有效）
            targetRow.Buttons.Add(item);
        }

        FloatingTriggerRows.RemoveAt(index);

        // 重新计算行索引
        for (int i = 0; i < FloatingTriggerRows.Count; i++)
        {
            FloatingTriggerRows[i].RowIndex = i + 1;
        }

        PersistFloatingTriggerRows();
        return true;
    }

    public bool MoveFloatingTrigger(string buttonId, int targetRowIndex, int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(buttonId) || FloatingTriggerRows.Count == 0)
        {
            return false;
        }

        targetRowIndex = Math.Clamp(targetRowIndex, 0, FloatingTriggerRows.Count - 1);
        var sourceRow = FloatingTriggerRows.FirstOrDefault(r => r.Buttons.Any(b => b.ButtonId == buttonId));

        // 如果按钮不在任何行中（如在按钮池中），尝试从按钮池添加
        if (sourceRow == null)
        {
            return AddTriggerFromPool(buttonId, targetRowIndex, targetIndex);
        }

        var item = sourceRow.Buttons.First(b => b.ButtonId == buttonId);
        var sourceIndex = sourceRow.Buttons.IndexOf(item);
        var destinationRow = FloatingTriggerRows[targetRowIndex];

        if (ReferenceEquals(sourceRow, destinationRow))
        {
            if (targetIndex > sourceIndex)
            {
                targetIndex--;
            }
            targetIndex = Math.Clamp(targetIndex, 0, destinationRow.Buttons.Count - 1);
            if (targetIndex == sourceIndex)
            {
                return false;
            }

            sourceRow.Buttons.Move(sourceIndex, targetIndex);
            PersistFloatingTriggerRows();
            return true;
        }

        sourceRow.Buttons.RemoveAt(sourceIndex);
        targetIndex = Math.Clamp(targetIndex, 0, destinationRow.Buttons.Count);
        destinationRow.Buttons.Insert(targetIndex, item);
        PersistFloatingTriggerRows();
        return true;
    }

    /// <summary>
    /// 将已注册但不在行中的按钮添加到指定行（用于拖拽等场景）
    /// </summary>
    public bool AddTriggerFromPool(string buttonId, int targetRowIndex, int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(buttonId) || FloatingTriggerRows.Count == 0)
        {
            return false;
        }

        var entry = _floatingWindowService.Entries.FirstOrDefault(e => e.ButtonId == buttonId);
        if (entry == null)
        {
            return false;
        }

        targetRowIndex = Math.Clamp(targetRowIndex, 0, FloatingTriggerRows.Count - 1);
        var destinationRow = FloatingTriggerRows[targetRowIndex];
        targetIndex = Math.Clamp(targetIndex, 0, destinationRow.Buttons.Count);

        var profile = CurrentFloatingWindowProfile;
        if (!profile.FloatingWindowButtonRulesets.TryGetValue(buttonId, out var btnConfig))
        {
            btnConfig = new ButtonRulesetConfig();
            profile.FloatingWindowButtonRulesets[buttonId] = btnConfig;
        }
        // 重新添加到行中时恢复可见
        btnConfig.IsVisible = true;

        var item = new FloatingTriggerItem
        {
            ButtonId = entry.ButtonId,
            Icon = entry.Icon,
            ButtonName = entry.LayoutName,
            Config = btnConfig
        };
        item.Config.PropertyChanged += OnButtonConfigPropertyChanged;
        if (item.Config.HidingRules is INotifyPropertyChanged btnHidingRules)
        {
            btnHidingRules.PropertyChanged += OnButtonConfigPropertyChanged;
        }

        destinationRow.Buttons.Insert(targetIndex, item);
        PersistFloatingTriggerRows();
        return true;
    }

    public void PersistFloatingTriggerRows(bool updateWindow = true, bool forceSave = true)
    {
        var profile = CurrentFloatingWindowProfile;
        var newRows = FloatingTriggerRows
            .Select(row => row.Buttons.Select(x => x.ButtonId).ToList())
            .ToList();
        var newOrder = newRows
            .SelectMany(row => row)
            .ToList();

        var rowsChanged = !AreRowsEqual(profile.FloatingWindowButtonRows, newRows);
        var orderChanged = !(profile.FloatingWindowButtonOrder ?? []).SequenceEqual(newOrder);

        if (rowsChanged)
        {
            profile.FloatingWindowButtonRows = newRows;
        }

        if (orderChanged)
        {
            profile.FloatingWindowButtonOrder = newOrder;
        }

        // 同步行规则集：确保 FloatingWindowRowRulesets 与行数一致
        var rowRulesets = profile.FloatingWindowRowRulesets;
        while (rowRulesets.Count < FloatingTriggerRows.Count)
        {
            rowRulesets.Add(new RowRulesetConfig());
        }
        while (rowRulesets.Count > FloatingTriggerRows.Count)
        {
            // 注销被移除行规则集的事件
            var removedRowRuleset = rowRulesets[rowRulesets.Count - 1];
            removedRowRuleset.PropertyChanged -= OnRowRulesetPropertyChanged;
            if (removedRowRuleset.HidingRules is INotifyPropertyChanged removedHidingRules)
            {
                removedHidingRules.PropertyChanged -= OnRowRulesetPropertyChanged;
            }
            rowRulesets.RemoveAt(rowRulesets.Count - 1);
        }
        // 同步每行的 RowRuleset 引用（确保ViewModel中的修改反映到profile）
        for (int i = 0; i < FloatingTriggerRows.Count; i++)
        {
            var vmRow = FloatingTriggerRows[i];
            if (!ReferenceEquals(vmRow.RowRuleset, rowRulesets[i]))
            {
                // RowRuleset 引用变更时，重新注册事件
                vmRow.RowRuleset.PropertyChanged -= OnRowRulesetPropertyChanged;
                if (vmRow.RowRuleset.HidingRules is INotifyPropertyChanged oldHidingRules)
                {
                    oldHidingRules.PropertyChanged -= OnRowRulesetPropertyChanged;
                }
                vmRow.RowRuleset = rowRulesets[i];
                vmRow.RowRuleset.PropertyChanged += OnRowRulesetPropertyChanged;
                if (vmRow.RowRuleset.HidingRules is INotifyPropertyChanged newHidingRules)
                {
                    newHidingRules.PropertyChanged += OnRowRulesetPropertyChanged;
                }
            }
        }

        // 清理不再使用的按钮规则集配置
        var usedButtonIds = new HashSet<string>(newOrder);
        var staleButtonIds = profile.FloatingWindowButtonRulesets.Keys.Where(id => !usedButtonIds.Contains(id)).ToList();
        foreach (var staleId in staleButtonIds)
        {
            profile.FloatingWindowButtonRulesets.Remove(staleId);
        }

        if (forceSave)
        {
            _floatingWindowService.ProfileManager.SaveProfile();
        }

        if (updateWindow)
        {
            _floatingWindowService.UpdateWindowState();
        }
    }

    public void AddFloatingWindowProfile(string? name = null)
    {
        var newName = _floatingWindowService.ProfileManager.CreateProfile(name);
        RefreshFloatingWindowProfiles();
        SelectedFloatingWindowProfile = newName;
        SwitchFloatingWindowProfile(newName);
    }

    public void RemoveFloatingWindowProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        if (_floatingWindowService.ProfileManager.RemoveProfile(profileName))
        {
            RefreshFloatingWindowProfiles();
            // 如果删除的是当前方案，切换到 Default
            if (string.Equals(SelectedFloatingWindowProfile, profileName, StringComparison.OrdinalIgnoreCase))
            {
                SwitchFloatingWindowProfile("Default");
            }
        }
    }

    public void SwitchFloatingWindowProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        _floatingWindowService.SwitchToProfile(profileName);
        SelectedFloatingWindowProfile = profileName;
        RefreshFloatingTriggers();

        // 通知 UI 重新注册 Profile 属性变更事件监听
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Profile 对象发生变化时触发（切换方案后需要重新注册事件监听）
    /// </summary>
    public event EventHandler? ProfileChanged;

    public void Dispose()
    {
        _floatingWindowService.EntriesChanged -= _entriesChangedHandler;
    }

    private static bool AreRowsEqual(IReadOnlyList<List<string>>? left, IReadOnlyList<List<string>> right)
    {
        if (left == null)
        {
            return right.Count == 0;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].SequenceEqual(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public bool CheckFfmpegExists()
    {
        try
        {
            return File.Exists(DependencyPaths.GetFfmpegPath());
        }
        catch
        {
            return false;
        }
    }

    public bool CheckFaceModelsExists()
    {
        try
        {
            return DependencyPaths.HasFaceRecognitionDependencies();
        }
        catch
        {
            return false;
        }
    }

    public void RefreshDownloadButtonStates()
    {
        if (IsDownloadInProgress)
        {
            IsFfmpegDownloadEnabled = false;
            IsFaceModelsDownloadEnabled = false;
            IsVoskWorkerDownloadEnabled = false;
            IsSpeechRecognitionActionEnabled = false;
            IsSpeechRecognitionModelSelectionEnabled = false;
            return;
        }

        IsFfmpegDownloadEnabled = !CheckFfmpegExists();
        IsFaceModelsDownloadEnabled = !CheckFaceModelsExists();
        IsVoskWorkerDownloadEnabled = !DependencyPaths.HasDownloadedSpeechRecognitionWorker();
        IsSpeechRecognitionModelSelectionEnabled = true;
        if (SelectedSpeechRecognitionModel is { } model)
        {
            var installed = DependencyPaths.IsSpeechRecognitionModelInstalled(model.ModelName);
            SpeechRecognitionActionText = installed ? "删除" : "下载";
            IsSpeechRecognitionActionEnabled = true;
        }
        else
        {
            SpeechRecognitionActionText = "下载";
            IsSpeechRecognitionActionEnabled = false;
        }
    }

    partial void OnSelectedSpeechRecognitionModelChanged(SpeechRecognitionDownloadOption? value)
    {
        RefreshDownloadButtonStates();
    }

    public bool IsSelectedSpeechRecognitionModelInstalled() =>
        SelectedSpeechRecognitionModel is { } model &&
        DependencyPaths.IsSpeechRecognitionModelInstalled(model.ModelName);

    public async Task<bool> DownloadVoskWorkerAsync(
        Func<Task> onError,
        Func<Task> onMd5Error)
    {
        if (!IsVoskWorkerDownloadEnabled || !await TryBeginDownloadAsync())
        {
            return false;
        }

        string? zipPath = null;
        string? stagingPath = null;
        string? workerPath = null;

        try
        {
            ShowDownloadProgress = true;
            DownloadProgress = 0;
            DownloadStatusText = "正在下载语音识别服务 - 0%";

            DependencyPaths.EnsureDependencyDirectories();
            var dependencyRoot = DependencyPaths.GetDependencyRoot();
            zipPath = Path.Combine(dependencyRoot, VoskWorkerZipFileName);
            stagingPath = Path.Combine(dependencyRoot, ".VoskWorker.extracting");
            workerPath = DependencyPaths.GetDownloadedSpeechRecognitionWorkerDirectory();

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromHours(1)
            };
            using var response = await httpClient.GetAsync(
                VoskWorkerUrl,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;
            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 1024];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        await UpdateProgressAsync((double)downloadedBytes / totalBytes * 100);
                    }
                }
            }

            await UpdateStatusAsync("正在校验语音识别服务 MD5…");
            var actualMd5 = await CalculateMd5Async(zipPath);
            if (!string.Equals(actualMd5, VoskWorkerMd5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                await onMd5Error();
                return false;
            }

            await UpdateStatusAsync("正在解压语音识别服务…");
            Directory.CreateDirectory(stagingPath);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingPath, true));
            NormalizeVoskWorkerLayout(stagingPath, workerPath);

            if (!DependencyPaths.HasDownloadedSpeechRecognitionWorker())
            {
                throw new InvalidDataException("解压完成，但 VoskWorker 文件不满足语音识别服务检测要求。");
            }

            File.Delete(zipPath);
            await UpdateStatusAsync("处理完成！");
            await Task.Delay(1000);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] VoskWorker 下载失败: {ex.Message}");
            if (zipPath is not null && File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            if (stagingPath is not null && Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }

            if (workerPath is not null && Directory.Exists(workerPath) &&
                !DependencyPaths.HasDownloadedSpeechRecognitionWorker())
            {
                Directory.Delete(workerPath, true);
            }

            await onError();
            return false;
        }
        finally
        {
            try
            {
                CompleteDownload();
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
    }

    private static void NormalizeVoskWorkerLayout(string stagingPath, string workerPath)
    {
        var sourcePath = Directory
            .EnumerateDirectories(stagingPath, "*", SearchOption.AllDirectories)
            .Prepend(stagingPath)
            .FirstOrDefault(DependencyPaths.IsSpeechRecognitionWorkerInstallationDirectory)
            ?? throw new InvalidDataException("压缩包中找不到完整的 VoskWorker 文件夹。");

        if (Directory.Exists(workerPath))
        {
            Directory.Delete(workerPath, true);
        }

        if (string.Equals(sourcePath, stagingPath, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(stagingPath, workerPath);
        }
        else
        {
            Directory.Move(sourcePath, workerPath);
            Directory.Delete(stagingPath, true);
        }
    }

    public async Task<bool> DownloadSpeechRecognitionModelAsync(
        Func<Task> onError,
        Func<Task> onMd5Error)
    {
        var model = SelectedSpeechRecognitionModel;
        if (model is null || !IsSpeechRecognitionActionEnabled ||
            !await TryBeginDownloadAsync())
        {
            return false;
        }

        string? zipPath = null;
        string? stagingPath = null;
        string? modelPath = null;

        try
        {
            ShowDownloadProgress = true;
            DownloadProgress = 0;
            DownloadStatusText = $"正在下载 {model.DisplayName} - 0%";

            DependencyPaths.EnsureDependencyDirectories();
            var dependencyRoot = DependencyPaths.GetDependencyRoot();
            modelPath = DependencyPaths.GetSpeechRecognitionModelDirectory(model.ModelName);
            zipPath = Path.Combine(dependencyRoot, $"{model.ModelName}.zip");
            stagingPath = Path.Combine(dependencyRoot, $".{model.ModelName}.extracting");

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromHours(2)
            };
            using var response = await httpClient.GetAsync(
                model.Url,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;
            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 1024];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        await UpdateProgressAsync((double)downloadedBytes / totalBytes * 100);
                    }
                }
            }

            await UpdateStatusAsync("正在校验模型 MD5…");
            var actualMd5 = await CalculateMd5Async(zipPath);
            if (!string.Equals(actualMd5, model.ExpectedMd5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                await onMd5Error();
                return false;
            }

            await UpdateStatusAsync("正在解压语音识别服务与模型…");
            Directory.CreateDirectory(stagingPath);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingPath, true));
            NormalizeSpeechRecognitionModelLayout(stagingPath, modelPath);

            if (!DependencyPaths.IsSpeechRecognitionModelInstalled(model.ModelName))
            {
                throw new InvalidDataException("解压完成，但模型文件不满足语音识别检测要求。");
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            await UpdateStatusAsync("处理完成！");
            await Task.Delay(1000);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 语音模型下载失败: {ex.Message}");
            if (zipPath is not null && File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            if (stagingPath is not null && Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }

            if (modelPath is not null && Directory.Exists(modelPath) &&
                !DependencyPaths.IsSpeechRecognitionModelInstalled(model.ModelName))
            {
                Directory.Delete(modelPath, true);
            }

            await onError();
            return false;
        }
        finally
        {
            try
            {
                CompleteDownload();
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
    }

    public async Task<bool> DeleteSelectedSpeechRecognitionModelAsync(Func<Task> onError)
    {
        var model = SelectedSpeechRecognitionModel;
        if (model is null || !IsSelectedSpeechRecognitionModelInstalled() ||
            !await TryBeginDownloadAsync())
        {
            return false;
        }

        try
        {
            var modelPath = DependencyPaths.GetSpeechRecognitionModelDirectory(model.ModelName);
            await UpdateStatusAsync("正在删除语音识别服务与模型…");
            await Task.Run(() => Directory.Delete(modelPath, true));
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 删除语音模型失败: {ex.Message}");
            await onError();
            return false;
        }
        finally
        {
            try
            {
                CompleteDownload();
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
    }

    private static void NormalizeSpeechRecognitionModelLayout(string stagingPath, string modelPath)
    {
        var directories = Directory.GetDirectories(stagingPath);
        var files = Directory.GetFiles(stagingPath);
        var sourcePath = directories.Length == 1 && files.Length == 0
            ? directories[0]
            : stagingPath;

        if (Directory.Exists(modelPath))
        {
            Directory.Delete(modelPath, true);
        }

        if (string.Equals(sourcePath, stagingPath, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(stagingPath, modelPath);
        }
        else
        {
            Directory.Move(sourcePath, modelPath);
            Directory.Delete(stagingPath, true);
        }
    }

    public async Task<bool> DownloadFfmpegAsync(Func<Task> onError, Func<Task> onMd5Error)
    {
        if (!IsFfmpegDownloadEnabled || !await TryBeginDownloadAsync()) return false;

        string? tempPath = null;

        try
        {
            ShowDownloadProgress = true;
            DownloadProgress = 0;
            DownloadStatusText = "正在下载 - 0%";

            DependencyPaths.EnsureDependencyDirectories();
            var dependencyRoot = DependencyPaths.GetDependencyRoot();
            var downloadTempPath = Path.Combine(dependencyRoot, TempFileName);
            tempPath = downloadTempPath;
            var targetPath = DependencyPaths.GetFfmpegPath();

            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(downloadTempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[4 * 1024 * 1024];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (double)downloadedBytes / totalBytes * 100;
                    await UpdateProgressAsync(progress);
                }
            }

            fileStream.Close();
            await Task.Delay(500);
            await UpdateStatusAsync("正在校验MD5…");

            var actualMd5 = await CalculateMd5Async(downloadTempPath);
            if (!string.Equals(actualMd5, ExpectedMd5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(downloadTempPath);
                await onMd5Error();
                return false;
            }

            await Task.Delay(500);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(downloadTempPath, targetPath);
            await Task.Delay(500);
            ShowDownloadProgress = false;

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 下载失败: {ex.Message}");

            if (tempPath != null && File.Exists(tempPath))
            {
                await Task.Delay(2000);
                File.Delete(tempPath);
            }

            await onError();
            return false;
        }
        finally
        {
            try
            {
                CompleteDownload();
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
    }

    public async Task<bool> DownloadFaceModelsAsync(Func<Task> onError, Func<Task> onMd5Error)
    {
        if (!IsFaceModelsDownloadEnabled || !await TryBeginDownloadAsync()) return false;

        string? zipPath = null;

        try
        {
            ShowDownloadProgress = true;
            DownloadProgress = 0;

            DependencyPaths.EnsureDependencyDirectories();
            var dependencyRoot = DependencyPaths.GetDependencyRoot();
            var archivePath = Path.Combine(dependencyRoot, FaceZipFileName);
            zipPath = archivePath;

            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(FaceModelsUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(archivePath, FileMode.Create))
            {
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        await UpdateProgressAsync((double)downloadedBytes / totalBytes * 100);
                    }
                }
            }

            await UpdateStatusAsync("正在校验模型 MD5…");
            var actualMd5 = await CalculateMd5Async(archivePath);
            if (!string.Equals(actualMd5, FaceModelsMd5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archivePath);
                await onMd5Error();
                return false;
            }

            await UpdateStatusAsync("正在解压并安装模型文件…");
            await Task.Run(() => FaceDependencyInstaller.InstallArchive(archivePath, dependencyRoot));

            if (File.Exists(archivePath)) File.Delete(archivePath);

            await UpdateStatusAsync("处理完成！");
            await Task.Delay(1000);
            ShowDownloadProgress = false;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 下载模型失败: {ex.Message}");
            if (zipPath != null && File.Exists(zipPath)) File.Delete(zipPath);
            await onError();
            return false;
        }
        finally
        {
            try
            {
                CompleteDownload();
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
    }

    private async Task<bool> TryBeginDownloadAsync()
    {
        if (!await _downloadSemaphore.WaitAsync(0))
        {
            return false;
        }

        IsDownloadInProgress = true;
        IsFfmpegDownloadEnabled = false;
        IsFaceModelsDownloadEnabled = false;
        IsVoskWorkerDownloadEnabled = false;
        IsSpeechRecognitionActionEnabled = false;
        IsSpeechRecognitionModelSelectionEnabled = false;
        return true;
    }

    private void CompleteDownload()
    {
        ShowDownloadProgress = false;
        DownloadProgress = 0;
        DownloadStatusText = string.Empty;
        IsDownloadInProgress = false;
        RefreshDownloadButtonStates();
    }

    private async Task UpdateProgressAsync(double progress)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadProgress = progress;
            DownloadStatusText = $"正在下载 - {progress:F0}%";
        });
    }

    private async Task UpdateStatusAsync(string status)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { DownloadStatusText = status; });
    }

    private static async Task<string> CalculateMd5Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await MD5.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }
}

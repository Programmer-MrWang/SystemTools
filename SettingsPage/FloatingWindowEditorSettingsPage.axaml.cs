using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls.Ruleset;
using ClassIsland.Core.Models.Ruleset;
using ClassIsland.Shared;
using SystemTools.ConfigHandlers;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo("systemtools.settings.floating", "悬浮窗编辑", "\uEA37", "\uEA37")]
public partial class FloatingWindowEditorSettingsPage : SettingsPageBase
{
    public FloatingWindowEditorSettingsPage()
    {
        if (GlobalConstants.MainConfig == null)
            GlobalConstants.MainConfig = new MainConfigHandler(GlobalConstants.PluginConfigFolder
                                                               ?? Path.Combine(
                                                                   Environment.GetFolderPath(Environment.SpecialFolder
                                                                       .LocalApplicationData), "ClassIsland", "Plugins",
                                                                   "SystemTools"));

        ViewModel = new SystemToolsSettingsViewModel(GlobalConstants.MainConfig,
            IAppHost.GetService<FloatingWindowService>());
        DataContext = this;
        InitializeComponent();

        ViewModel.RefreshFloatingWindowProfiles();
        ViewModel.RefreshFloatingTriggers();
        ViewModel.CurrentFloatingWindowProfile.PropertyChanged += OnProfilePropertyChanged;
        ViewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
        ViewModel.ProfileChanged += OnViewModelProfileChanged;

        // 注册全局设置变更监听（ShowFloatingWindow 和规则集不随方案切换）
        RegisterHidingRulesEvents();
    }

    public SystemToolsSettingsViewModel ViewModel { get; }

    private bool _isDisposed;

    private Point? _floatingDragStartPoint;
    private Border? _floatingDragSourceBorder;

    // ===== 规则集 Drawer 状态 =====
    private enum RulesetTargetType { Button, Row, Window }
    private RulesetTargetType _currentRulesetTarget;
    private FloatingTriggerItem? _currentButtonTarget;
    private FloatingTriggerRow? _currentRowTarget;

    // Drawer 内的控件引用
    private ToggleSwitch? _drawerIsVisibleToggle;
    private ToggleSwitch? _drawerHideOnRuleToggle;
    private RulesetControl? _drawerRulesetControl;

    // 当前 Drawer 中正在编辑的规则集，用于实时监听其变化
    private Ruleset? _currentDrawerRuleset;
    private readonly List<INotifyPropertyChanged> _rulesetPropertyListeners = new();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_isDisposed)
        {
            return;
        }

        ViewModel.CurrentFloatingWindowProfile.PropertyChanged -= OnProfilePropertyChanged;
        ViewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        ViewModel.ProfileChanged -= OnViewModelProfileChanged;

        UnregisterHidingRulesEvents();
        DetachRulesetListeners();

        ViewModel.Dispose();
        _isDisposed = true;
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FloatingWindowProfile.FloatingWindowScale)
            or nameof(FloatingWindowProfile.FloatingWindowIconSize)
            or nameof(FloatingWindowProfile.FloatingWindowTextSize)
            or nameof(FloatingWindowProfile.FloatingWindowOpacity)
            or nameof(FloatingWindowProfile.FloatingWindowShadowEnabled)
            or nameof(FloatingWindowProfile.FloatingWindowLayer)
            or nameof(FloatingWindowProfile.FloatingWindowLayerRecheckMode)
            or nameof(FloatingWindowProfile.FloatingWindowDragHandleAlwaysVisible)
            or nameof(FloatingWindowProfile.FloatingWindowHorizontal))
        {
            IAppHost.GetService<FloatingWindowService>().ProfileManager.SaveProfile();
            IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
        }
    }

    /// <summary>
    /// 重新注册 Profile 属性变更事件监听（切换方案后需要重新注册）
    /// </summary>
    public void ReattachProfilePropertyChanged()
    {
        ViewModel.CurrentFloatingWindowProfile.PropertyChanged -= OnProfilePropertyChanged;
        ViewModel.CurrentFloatingWindowProfile.PropertyChanged += OnProfilePropertyChanged;

        // 重新注册悬浮窗规则集变更监听
        UnregisterHidingRulesEvents();
        RegisterHidingRulesEvents();
    }

    private void RegisterHidingRulesEvents()
    {
        if (ViewModel.Settings.FloatingWindowRuleset is INotifyPropertyChanged hidingRules)
        {
            hidingRules.PropertyChanged += OnHidingRulesPropertyChanged;
        }
    }

    private void UnregisterHidingRulesEvents()
    {
        if (ViewModel.Settings.FloatingWindowRuleset is INotifyPropertyChanged hidingRules)
        {
            hidingRules.PropertyChanged -= OnHidingRulesPropertyChanged;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainConfigData.FloatingWindowTheme))
        {
            GlobalConstants.MainConfig?.Save();
            IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
        }
        else if (e.PropertyName is nameof(MainConfigData.ShowFloatingWindow)
            or nameof(MainConfigData.FloatingWindowRulesetEnabled))
        {
            GlobalConstants.MainConfig?.Save();
            IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
            IAppHost.TryGetService<IRulesetService>()?.NotifyStatusChanged();
        }
        else if (e.PropertyName == nameof(MainConfigData.FloatingWindowRuleset))
        {
            // Ruleset 对象被替换时，重新注册事件
            UnregisterHidingRulesEvents();
            RegisterHidingRulesEvents();
            GlobalConstants.MainConfig?.Save();
        }
    }

    private void OnViewModelProfileChanged(object? sender, EventArgs e)
    {
        ReattachProfilePropertyChanged();
    }

    private void OnHidingRulesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 避免规则集 State 变化导致递归通知
        if (e.PropertyName == nameof(Rule.State))
        {
            return;
        }

        GlobalConstants.MainConfig?.Save();
        IAppHost.TryGetService<IRulesetService>()?.NotifyStatusChanged();
    }

    private void OnFloatingWindowVisibleToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle)
        {
            return;
        }

        var service = IAppHost.GetService<FloatingWindowService>();
        var config = ViewModel.Settings;

        // 没有可用按钮时强制隐藏
        var shouldShow = toggle.IsChecked == true && service.Entries.Count > 0;
        config.ShowFloatingWindow = shouldShow;

        // 同步 ToggleSwitch 状态（可能被强制隐藏）
        if (toggle.IsChecked != shouldShow)
        {
            toggle.IsChecked = shouldShow;
        }

        GlobalConstants.MainConfig?.Save();
        service.UpdateWindowState();
    }

    private void OnFloatingWindowProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.SelectedItem is not string profileName)
        {
            return;
        }

        ViewModel.SwitchFloatingWindowProfile(profileName);
    }

    private void OnToggleFloatingWindowProfileClick(object? sender, RoutedEventArgs e)
    {
        IAppHost.GetService<FloatingWindowService>().ToggleWindowProfile();
        ViewModel.RefreshFloatingWindowProfiles();
        ViewModel.RefreshFloatingTriggers();
    }

    private void OnAddFloatingWindowProfileClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddFloatingWindowProfile();
    }

    private void OnRemoveCurrentProfileClick(object? sender, RoutedEventArgs e)
    {
        var currentName = ViewModel.SelectedFloatingWindowProfile;
        if (string.IsNullOrWhiteSpace(currentName))
        {
            return;
        }

        ViewModel.RemoveFloatingWindowProfile(currentName);
    }

    private void OnAddFloatingTriggerRowClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddFloatingTriggerRow();
    }

    private void OnRemoveFloatingTriggerRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FloatingTriggerRow row })
        {
            return;
        }

        if (ViewModel.FloatingTriggerRows.Count <= 1)
        {
            return;
        }

        _ = ViewModel.RemoveFloatingTriggerRow(row);
    }

    private void ButtonOpenFloatingWindowRuleset_OnClick(object? sender, RoutedEventArgs e)
    {
        _currentRulesetTarget = RulesetTargetType.Window;
        _currentButtonTarget = null;
        _currentRowTarget = null;

        var config = ViewModel.Settings;
        OpenRulesetDrawer(config.FloatingWindowRuleset, true, config.FloatingWindowRulesetEnabled);
    }

    /// <summary>
    /// 打开规则集 Drawer，包含 IsVisible/HideOnRule 开关和规则集编辑器（参照 ClassIsland）
    /// </summary>
    private void OpenRulesetDrawer(ClassIsland.Core.Models.Ruleset.Ruleset ruleset, bool isVisible, bool hideOnRule)
    {
        // 先清理上一次 Drawer 的规则集监听，避免内存泄漏和重复通知
        DetachRulesetListeners();

        // 每次打开时动态构建 Drawer 内容，避免资源单例问题
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        // 开关面板
        var togglesPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 0, 0, 8) };

        _drawerIsVisibleToggle = new ToggleSwitch
        {
            OnContent = "显示",
            OffContent = "隐藏",
            IsChecked = isVisible,
            IsVisible = _currentRulesetTarget != RulesetTargetType.Window
        };
        ToolTip.SetTip(_drawerIsVisibleToggle, "控制此项目是否显示");
        _drawerIsVisibleToggle.IsCheckedChanged += OnDrawerIsVisibleChanged;

        _drawerHideOnRuleToggle = new ToggleSwitch
        {
            OnContent = "按规则隐藏",
            OffContent = "禁用规则",
            IsChecked = hideOnRule
        };
        ToolTip.SetTip(_drawerHideOnRuleToggle, "启用后，满足规则集条件时自动隐藏");
        _drawerHideOnRuleToggle.IsCheckedChanged += OnDrawerHideOnRuleChanged;

        togglesPanel.Children.Add(_drawerIsVisibleToggle);
        togglesPanel.Children.Add(_drawerHideOnRuleToggle);
        panel.Children.Add(togglesPanel);

        // 规则集编辑器
        _drawerRulesetControl = new RulesetControl { Classes = { "in-drawer" }, Ruleset = ruleset };
        panel.Children.Add(_drawerRulesetControl);

        // 监听规则集内容变化，编辑时实时刷新悬浮窗状态
        AttachRulesetListeners(ruleset);

        // 将内容放入 Resources 并打开 Drawer
        this.Resources["RulesetDrawerContent"] = panel;
        OpenDrawer("RulesetDrawerContent");
    }

    private void OnDrawerIsVisibleChanged(object? sender, RoutedEventArgs e)
    {
        var value = _drawerIsVisibleToggle?.IsChecked == true;

        switch (_currentRulesetTarget)
        {
            case RulesetTargetType.Button when _currentButtonTarget != null:
                _currentButtonTarget.Config.IsVisible = value;
                break;
            case RulesetTargetType.Row when _currentRowTarget != null:
                _currentRowTarget.RowRuleset.IsVisible = value;
                break;
        }

        IAppHost.GetService<FloatingWindowService>().ProfileManager.SaveProfile();
        IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
        NotifyRulesetStatusChanged();
    }

    private void OnDrawerHideOnRuleChanged(object? sender, RoutedEventArgs e)
    {
        var value = _drawerHideOnRuleToggle?.IsChecked == true;

        switch (_currentRulesetTarget)
        {
            case RulesetTargetType.Button when _currentButtonTarget != null:
                _currentButtonTarget.Config.HideOnRule = value;
                break;
            case RulesetTargetType.Row when _currentRowTarget != null:
                _currentRowTarget.RowRuleset.HideOnRule = value;
                break;
            case RulesetTargetType.Window:
                ViewModel.Settings.FloatingWindowRulesetEnabled = value;
                GlobalConstants.MainConfig?.Save();
                break;
        }

        IAppHost.GetService<FloatingWindowService>().ProfileManager.SaveProfile();
        IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
        NotifyRulesetStatusChanged();
    }

    private void NotifyRulesetStatusChanged()
    {
        IAppHost.TryGetService<IRulesetService>()?.NotifyStatusChanged();
    }

    private void AttachRulesetListeners(Ruleset ruleset)
    {
        DetachRulesetListeners();
        _currentDrawerRuleset = ruleset;

        AddRulesetPropertyListener(ruleset);
        ruleset.Groups.CollectionChanged += OnRulesetGroupsCollectionChanged;

        foreach (var group in ruleset.Groups)
        {
            AddRulesetPropertyListener(group);
            group.Rules.CollectionChanged += OnRulesetRulesCollectionChanged;
            foreach (var rule in group.Rules)
            {
                AddRulesetPropertyListener(rule);
            }
        }
    }

    private void DetachRulesetListeners()
    {
        foreach (var listener in _rulesetPropertyListeners)
        {
            listener.PropertyChanged -= OnRulesetPropertyChanged;
        }
        _rulesetPropertyListeners.Clear();

        if (_currentDrawerRuleset != null)
        {
            _currentDrawerRuleset.Groups.CollectionChanged -= OnRulesetGroupsCollectionChanged;
            foreach (var group in _currentDrawerRuleset.Groups)
            {
                group.Rules.CollectionChanged -= OnRulesetRulesCollectionChanged;
            }
            _currentDrawerRuleset = null;
        }
    }

    private void AddRulesetPropertyListener(INotifyPropertyChanged listener)
    {
        listener.PropertyChanged += OnRulesetPropertyChanged;
        _rulesetPropertyListeners.Add(listener);
    }

    private void OnRulesetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 规则集求值时会写入 State，避免因此递归触发通知
        if (e.PropertyName == nameof(Rule.State))
        {
            return;
        }

        NotifyRulesetStatusChanged();
        IAppHost.TryGetService<FloatingWindowService>()?.UpdateWindowState();
    }

    private void OnRulesetGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentDrawerRuleset == null)
        {
            return;
        }

        var ruleset = _currentDrawerRuleset;
        DetachRulesetListeners();
        AttachRulesetListeners(ruleset);

        NotifyRulesetStatusChanged();
        IAppHost.TryGetService<FloatingWindowService>()?.UpdateWindowState();
    }

    private void OnRulesetRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentDrawerRuleset == null)
        {
            return;
        }

        var ruleset = _currentDrawerRuleset;
        DetachRulesetListeners();
        AttachRulesetListeners(ruleset);

        NotifyRulesetStatusChanged();
        IAppHost.TryGetService<FloatingWindowService>()?.UpdateWindowState();
    }

    // ===== 选中状态处理 =====

    private void OnFloatingTriggerItemSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FloatingTriggerItem item })
        {
            return;
        }

        _currentRulesetTarget = RulesetTargetType.Button;
        _currentButtonTarget = item;
        _currentRowTarget = null;

        OpenRulesetDrawer(item.Config.HidingRules, item.Config.IsVisible, item.Config.HideOnRule);
    }

    private void OnRowRulesetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FloatingTriggerRow row })
        {
            return;
        }

        _currentRulesetTarget = RulesetTargetType.Row;
        _currentButtonTarget = null;
        _currentRowTarget = row;

        OpenRulesetDrawer(row.RowRuleset.HidingRules, row.RowRuleset.IsVisible, row.RowRuleset.HideOnRule);
    }

    private void OnInsertRowBelowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FloatingTriggerRow row })
        {
            return;
        }

        var index = ViewModel.FloatingTriggerRows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        ViewModel.InsertFloatingTriggerRow(index + 1);
    }

    private void OnFloatingTriggerItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || !e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _floatingDragSourceBorder = border;
        _floatingDragStartPoint = e.GetPosition(border);
        e.Handled = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
    }

    private void OnFloatingTriggerItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _floatingDragSourceBorder = null;
        _floatingDragStartPoint = null;
    }

    private async void OnFloatingTriggerItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border border || _floatingDragSourceBorder != border || _floatingDragStartPoint == null)
        {
            return;
        }

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var now = e.GetPosition(border);
        if (Math.Abs(now.X - _floatingDragStartPoint.Value.X) + Math.Abs(now.Y - _floatingDragStartPoint.Value.Y) < 4)
        {
            return;
        }

        if (border.Tag is not string buttonId || string.IsNullOrWhiteSpace(buttonId))
        {
            return;
        }

        var data = new DataObject();
        data.Set("FloatingTriggerButtonId", buttonId);

        _floatingDragSourceBorder = null;
        _floatingDragStartPoint = null;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        e.Handled = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
    }

    private static bool TryGetDragButtonId(DragEventArgs e, out string buttonId)
    {
        buttonId = string.Empty;
        if (!e.Data.Contains("FloatingTriggerButtonId"))
        {
            return false;
        }

        buttonId = e.Data.Get("FloatingTriggerButtonId") as string ?? string.Empty;
        return !string.IsNullOrWhiteSpace(buttonId);
    }

    private int GetRowIndexFromControl(Control? control)
    {
        var current = control;
        while (current != null)
        {
            if (current.DataContext is FloatingTriggerRow row)
            {
                return ViewModel.FloatingTriggerRows.IndexOf(row);
            }

            current = current.GetVisualParent() as Control;
        }

        return -1;
    }

    private int GetRowInsertIndex(Control sender, FloatingTriggerRow row, DragEventArgs e)
    {
        if (row.Buttons.Count == 0)
        {
            return 0;
        }

        var pointer = e.GetPosition(sender);
        var itemBorders = sender.GetVisualDescendants()
            .OfType<Border>()
            .Where(x => x.DataContext is FloatingTriggerItem)
            .OrderBy(x => x.TranslatePoint(new Point(0, 0), sender)?.X ?? double.MaxValue)
            .ToList();

        for (var i = 0; i < itemBorders.Count; i++)
        {
            var topLeft = itemBorders[i].TranslatePoint(new Point(0, 0), sender);
            if (topLeft == null)
            {
                continue;
            }

            var center = topLeft.Value.X + itemBorders[i].Bounds.Width / 2;
            if (pointer.X <= center)
            {
                return i;
            }
        }

        return row.Buttons.Count;
    }

    private void OnFloatingTriggerRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDragButtonId(e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFloatingTriggerRowDrop(object? sender, DragEventArgs e)
    {
        if (!TryGetDragButtonId(e, out var buttonId) || sender is not Control senderControl)
        {
            return;
        }

        var rowIndex = GetRowIndexFromControl(senderControl);
        if (rowIndex < 0)
        {
            return;
        }

        var row = ViewModel.FloatingTriggerRows[rowIndex];
        var insertIndex = GetRowInsertIndex(senderControl, row, e);
        ViewModel.MoveFloatingTrigger(buttonId, rowIndex, insertIndex);
    }

    private void OnFloatingTriggerItemDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDragButtonId(e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFloatingTriggerItemDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not FloatingTriggerItem targetItem)
        {
            return;
        }

        if (!TryGetDragButtonId(e, out var buttonId))
        {
            return;
        }

        var rowIndex = GetRowIndexFromControl(border);
        if (rowIndex < 0)
        {
            return;
        }

        var row = ViewModel.FloatingTriggerRows[rowIndex];
        var targetIndex = row.Buttons.IndexOf(targetItem);
        if (targetIndex < 0)
        {
            return;
        }

        var pos = e.GetPosition(border);
        if (pos.X > border.Bounds.Width / 2)
        {
            targetIndex += 1;
        }

        ViewModel.MoveFloatingTrigger(buttonId, rowIndex, targetIndex);
    }
}

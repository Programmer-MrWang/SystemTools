using System;
using System.Collections.ObjectModel;
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
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls.Ruleset;
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

        // TouchDragThumb 继承自 Thumb，Thumb.OnPointerPressed 会设置 e.Handled = true 并捕获指针；
        // 组件库的 ListBoxItem 在 PointerPressed 中改变选择并触发 SelectionChanged → AddTriggerFromPool。
        // 用 Tunnel（外部→内部）路由 + handledEventsToo=true 让我们先于这些处理器捕获事件，
        // 确认来源后再决定是否拦截（设置 e.Handled=true）。PointerMoved/Released 注册到 TopLevel 确保总能收到。
        this.AddHandler(InputElement.PointerPressedEvent, OnPagePointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public SystemToolsSettingsViewModel ViewModel { get; }

    private bool _isDisposed;

    // ===== 拖拽状态 =====
    private const double DragThreshold = 4;
    private Point? _rowDragStartPoint;
    private FloatingTriggerRow? _rowDragSource;
    private Point? _buttonDragStartPoint;
    private FloatingTriggerItem? _buttonDragSourceItem;

    // ===== 规则集 Drawer 状态 =====
    private enum RulesetTargetType { Button, Row, Window }
    private RulesetTargetType _currentRulesetTarget;
    private FloatingTriggerItem? _currentButtonTarget;
    private FloatingTriggerRow? _currentRowTarget;

    // Drawer 内的控件引用
    private ToggleSwitch? _drawerIsVisibleToggle;
    private ToggleSwitch? _drawerHideOnRuleToggle;
    private RulesetControl? _drawerRulesetControl;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_isDisposed)
        {
            return;
        }

        // 清理拖拽状态和 TopLevel 事件处理器，避免泄漏
        CancelDrag();

        // 页面卸载时确保所有配置（包括规则集）都已保存
        IAppHost.GetService<FloatingWindowService>().ProfileManager.SaveProfile();

        ViewModel.CurrentFloatingWindowProfile.PropertyChanged -= OnProfilePropertyChanged;
        ViewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        ViewModel.ProfileChanged -= OnViewModelProfileChanged;

        UnregisterHidingRulesEvents();

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
        GlobalConstants.MainConfig?.Save();
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

    private void OnInsertRowBelowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var row = control.DataContext as FloatingTriggerRow;
        if (row == null)
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

    private void OnRemoveTriggerFromRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string buttonId)
        {
            return;
        }

        ViewModel.RemoveTriggerToPool(buttonId);
    }

    // ===== 规则集 Drawer（参照 ClassIsland，含 IsVisible/HideOnRule 开关） =====

    /// <summary>
    /// 按钮规则集按钮点击：打开该按钮的规则集 Drawer
    /// </summary>
    private void OnButtonRulesetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string buttonId)
            return;

        // 在所有行中查找该按钮
        var item = ViewModel.FloatingTriggerRows
            .SelectMany(r => r.Buttons)
            .FirstOrDefault(b => b.ButtonId == buttonId);
        if (item == null) return;

        ViewModel.SelectedFloatingTriggerItem = item;
        _currentRulesetTarget = RulesetTargetType.Button;
        _currentButtonTarget = item;
        _currentRowTarget = null;

        OpenRulesetDrawer(item.Config.HidingRules, item.Config.IsVisible, item.Config.HideOnRule);
    }

    /// <summary>
    /// 行规则集按钮点击：打开该行的规则集 Drawer
    /// </summary>
    private void OnRowRulesetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control)
            return;

        // 通过 DataContext 获取所属行
        var row = control.DataContext as FloatingTriggerRow;
        if (row == null) return;

        ViewModel.SelectedFloatingTriggerRow = row;
        _currentRulesetTarget = RulesetTargetType.Row;
        _currentRowTarget = row;
        _currentButtonTarget = null;

        OpenRulesetDrawer(row.RowRuleset.HidingRules, row.RowRuleset.IsVisible, row.RowRuleset.HideOnRule);
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
    }

    // ===== 选中状态处理 =====

    private void OnRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FloatingTriggerRow row)
        {
            ViewModel.SelectedFloatingTriggerRow = row;
        }
    }

    private void OnButtonSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FloatingTriggerItem item)
        {
            ViewModel.SelectedFloatingTriggerItem = item;
        }
    }

    private void OnAvailableItemSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not FloatingTriggerItem item)
        {
            return;
        }

        // 先清除选中状态，避免移除项时选择模型与集合冲突（ArgumentOutOfRangeException）
        var buttonId = item.ButtonId;
        listBox.SelectedItem = null;

        // 延迟执行添加操作，确保 SelectionChanged 事件处理完成后再修改集合
        // 否则 AvailableFloatingTriggerItems.Remove 会在选择模型迭代期间触发集合变更，导致 ArgumentOutOfRangeException
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // 点击组件库项：添加到第一行末尾
            if (ViewModel.FloatingTriggerRows.Count == 0)
            {
                ViewModel.AddFloatingTriggerRow();
            }
            ViewModel.AddTriggerFromPool(buttonId, 0, ViewModel.FloatingTriggerRows[0].Buttons.Count);
        });
    }

    // ===== 拖拽处理总览 =====
    // PointerPressed  : 页面级 AddHandler(Tunnel, handledEventsToo:true) → OnPagePointerPressed
    //                    先于 TouchDragThumb 捕获事件，判断来源后设置 e.Handled=true 阻止默认行为，
    //                    记录起始状态，并挂载 TopLevel 的 PointerMoved/Released/CaptureLost 监听。
    // PointerMoved    : TopLevel 级监听 → OnTopLevelPointerMoved
    //                    超过 DragThreshold 后调用 DragDrop.DoDragDrop 启动系统级拖拽。
    // PointerReleased : TopLevel 级监听 → OnTopLevelPointerReleased
    //                    组件库点击（未超过阈值）执行 AddTriggerFromPool；其他情况清理状态。
    // Drop            : 行区域 Border (OnRowDropBorderDrop) 和 行内按钮 ListBox (OnInnerButtonDrop)
    //                    分别处理行排序 / 按钮跨行移动 / 行内排序 / 组件库拖入。

    private TopLevel? _topLevel;
    private bool _topLevelHandlersAttached;

    private string? _buttonDragId; // 用于组件库/按钮拖拽

    /// <summary>
    /// 检查当前指针事件是否是"主要按键按下"（鼠标左键 / 触摸 / 笔），支持触摸拖拽
    /// </summary>
    private static bool IsPrimaryPointerPressed(PointerEventArgs e)
    {
        // 鼠标：检查左键；触摸/笔：PointerPressed 本身即表示接触中，
        // PointerMoved 也只在接触中才触发，无需额外检查按钮状态
        if (e.Pointer.Type == PointerType.Mouse)
            return e.GetCurrentPoint(null).Properties.IsLeftButtonPressed;
        return true;
    }

    /// <summary>
    /// 页面级 PointerPressed：判断来源是 TouchDragThumb 还是组件库项，记录拖拽起始状态
    /// </summary>
    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsPrimaryPointerPressed(e)) return;

        var source = e.Source as Control;
        if (source == null) return;

        // 1) 行/按钮把手：优先处理 TouchDragThumb（Tunnel 路由下始终拦截，避免 Thumb 捕获指针）
        var dragThumb = source.FindAncestorOfType<ClassIsland.Core.Controls.TouchDragThumb>();
        if (dragThumb != null)
        {
            // 行拖拽把手：DataContext 是 FloatingTriggerRow
            if (dragThumb.DataContext is FloatingTriggerRow row)
            {
                _rowDragSource = row;
                _rowDragStartPoint = e.GetPosition(null);
                e.Handled = true; // 始终阻断 Thumb 的默认处理
                AttachTopLevelDragHandlers();
                return;
            }

            // 按钮拖拽把手：DataContext 是 FloatingTriggerItem
            if (dragThumb.DataContext is FloatingTriggerItem thumbItem)
            {
                _buttonDragSourceItem = thumbItem;
                _buttonDragId = null; // 按钮项有对象引用，不需要 ButtonId 记录
                _buttonDragStartPoint = e.GetPosition(null);
                e.Handled = true; // 始终阻断 Thumb 的默认处理
                AttachTopLevelDragHandlers();
                return;
            }
        }

        // 2) 组件库项：检测 source 是 ListBoxAvailableItems 的后代
        var libItem = source.DataContext as FloatingTriggerItem;
        if (libItem != null && !string.IsNullOrEmpty(libItem.ButtonId))
        {
            // 通过查找 ListBox 的名称来确认是组件库，避免误判行内按钮
            var libListBox = source.FindAncestorOfType<ListBox>();
            if (libListBox != null && libListBox.Name == "ListBoxAvailableItems")
            {
                _buttonDragId = libItem.ButtonId;
                _buttonDragSourceItem = null; // 组件库项不需要记录对象引用
                _buttonDragStartPoint = e.GetPosition(null);
                e.Handled = true; // 阻断 ListBoxItem 的选择，避免 SelectionChanged 重复添加
                AttachTopLevelDragHandlers();
                return;
            }
        }
    }

    private void AttachTopLevelDragHandlers()
    {
        if (_topLevelHandlersAttached) return; // 避免重复注册

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel == null) return;

        _topLevel.AddHandler(InputElement.PointerMovedEvent, OnTopLevelPointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        _topLevel.AddHandler(InputElement.PointerReleasedEvent, OnTopLevelPointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        _topLevel.AddHandler(InputElement.PointerCaptureLostEvent, OnTopLevelPointerCaptureLost,
            RoutingStrategies.Bubble, handledEventsToo: true);

        _topLevelHandlersAttached = true;
    }

    private void DetachTopLevelDragHandlers()
    {
        if (_topLevel == null || !_topLevelHandlersAttached) return;

        _topLevel.RemoveHandler(InputElement.PointerMovedEvent, OnTopLevelPointerMoved);
        _topLevel.RemoveHandler(InputElement.PointerReleasedEvent, OnTopLevelPointerReleased);
        _topLevel.RemoveHandler(InputElement.PointerCaptureLostEvent, OnTopLevelPointerCaptureLost);
        _topLevel = null;
        _topLevelHandlersAttached = false;
    }

    private async void OnTopLevelPointerMoved(object? sender, PointerEventArgs e)
    {
        // 行拖拽
        if (_rowDragSource != null && _rowDragStartPoint != null)
        {
            if (!IsPrimaryPointerPressed(e))
            {
                CancelDrag();
                return;
            }

            var now = e.GetPosition(null);
            if (Math.Abs(now.X - _rowDragStartPoint.Value.X) + Math.Abs(now.Y - _rowDragStartPoint.Value.Y) < DragThreshold)
                return;

            var row = _rowDragSource;
            _rowDragSource = null;
            _rowDragStartPoint = null;
            DetachTopLevelDragHandlers();

            var data = new DataObject();
            data.Set("FloatingWindowRow", row);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            return;
        }

        // 按钮拖拽（行内按钮把手）或组件库拖拽
        if ((_buttonDragSourceItem != null || _buttonDragId != null) && _buttonDragStartPoint != null)
        {
            if (!IsPrimaryPointerPressed(e))
            {
                CancelDrag();
                return;
            }

            var now = e.GetPosition(null);
            if (Math.Abs(now.X - _buttonDragStartPoint.Value.X) + Math.Abs(now.Y - _buttonDragStartPoint.Value.Y) < DragThreshold)
                return;

            var data = new DataObject();
            var allowedEffects = DragDropEffects.Copy; // 组件库默认 Copy

            if (_buttonDragSourceItem != null)
            {
                // 行内按钮：需要 ButtonId + 源集合（用于跨行移动判断）
                var item = _buttonDragSourceItem;
                var row = ViewModel.FloatingTriggerRows.FirstOrDefault(r => r.Buttons.Contains(item));
                if (row == null)
                {
                    CancelDrag();
                    return;
                }
                data.Set("FloatingWindowButtonId", item.ButtonId);
                data.Set("FloatingWindowButtonSource", row.Buttons!);
                allowedEffects = DragDropEffects.Move; // 行内移动
            }
            else if (_buttonDragId != null)
            {
                // 组件库：只需要 ButtonId（sourceCollection = null 表示新增）
                data.Set("FloatingWindowButtonId", _buttonDragId);
                // allowedEffects = Copy（默认）
            }

            _buttonDragSourceItem = null;
            _buttonDragId = null;
            _buttonDragStartPoint = null;
            DetachTopLevelDragHandlers();

            await DragDrop.DoDragDrop(e, data, allowedEffects);
        }
    }

    private void OnTopLevelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // 组件库点击：按下后未超过拖拽阈值直接释放 → 执行点击添加
        if (_buttonDragId != null && _buttonDragStartPoint != null)
        {
            var pos = e.GetPosition(null);
            var dx = Math.Abs(pos.X - _buttonDragStartPoint.Value.X);
            var dy = Math.Abs(pos.Y - _buttonDragStartPoint.Value.Y);
            if (dx + dy < DragThreshold)
            {
                var buttonId = _buttonDragId;
                CancelDrag();

                // 点击添加：延迟执行避免与其他事件冲突
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ViewModel.FloatingTriggerRows.Count == 0)
                    {
                        ViewModel.AddFloatingTriggerRow();
                    }
                    ViewModel.AddTriggerFromPool(buttonId, 0, ViewModel.FloatingTriggerRows[0].Buttons.Count);
                });
                return;
            }
        }

        CancelDrag();
    }

    private void OnTopLevelPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CancelDrag();
    }

    private void CancelDrag()
    {
        _rowDragSource = null;
        _rowDragStartPoint = null;
        _buttonDragSourceItem = null;
        _buttonDragId = null;
        _buttonDragStartPoint = null;
        DetachTopLevelDragHandlers();
    }

    // ===== 行区域拖放处理 =====

    private void OnRowDropBorderDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("FloatingWindowRow"))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else if (e.Data.Contains("FloatingWindowButtonId"))
        {
            // 有 FloatingWindowButtonSource 表示从行内拖来（移动），没有表示从组件库拖入（复制）
            e.DragEffects = e.Data.Contains("FloatingWindowButtonSource")
                ? DragDropEffects.Move
                : DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnRowDropBorderDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        // 处理行拖拽排序
        if (e.Data.Contains("FloatingWindowRow"))
        {
            var sourceRow = e.Data.Get("FloatingWindowRow") as FloatingTriggerRow;
            if (sourceRow == null) return;

            var rowTargetIndex = FindTargetRowIndex(e, sender as Control);
            if (rowTargetIndex < 0) return;

            var sourceIndex = ViewModel.FloatingTriggerRows.IndexOf(sourceRow);
            if (sourceIndex < 0 || sourceIndex == rowTargetIndex) return;

            // 移动行
            ViewModel.FloatingTriggerRows.RemoveAt(sourceIndex);
            if (rowTargetIndex > sourceIndex) rowTargetIndex--;
            ViewModel.FloatingTriggerRows.Insert(rowTargetIndex, sourceRow);

            // 重新计算行索引
            for (int i = 0; i < ViewModel.FloatingTriggerRows.Count; i++)
            {
                ViewModel.FloatingTriggerRows[i].RowIndex = i + 1;
            }

            ViewModel.PersistFloatingTriggerRows();
            return;
        }

        // 处理按钮拖拽
        if (!e.Data.Contains("FloatingWindowButtonId")) return;

        var buttonId = e.Data.Get("FloatingWindowButtonId") as string;
        if (string.IsNullOrEmpty(buttonId)) return;

        var sourceCollection = e.Data.Get("FloatingWindowButtonSource") as ObservableCollection<FloatingTriggerItem>;

        // 确定目标行和位置
        if (ViewModel.FloatingTriggerRows.Count == 0)
        {
            ViewModel.AddFloatingTriggerRow();
        }

        var targetRowIndex = FindTargetRowIndex(e, sender as Control);
        if (targetRowIndex < 0) targetRowIndex = 0;
        targetRowIndex = System.Math.Clamp(targetRowIndex, 0, ViewModel.FloatingTriggerRows.Count - 1);
        var btnTargetIndex = ViewModel.FloatingTriggerRows[targetRowIndex].Buttons.Count;

        if (sourceCollection == null)
        {
            // 从组件库拖入
            ViewModel.AddTriggerFromPool(buttonId, targetRowIndex, btnTargetIndex);
        }
        else
        {
            // 从其他行拖入
            ViewModel.MoveFloatingTrigger(buttonId, targetRowIndex, btnTargetIndex);
        }
    }

    /// <summary>
    /// 根据拖放位置确定目标行索引（统一用 targetControl 作为坐标参考）
    /// </summary>
    private int FindTargetRowIndex(DragEventArgs e, Control? targetControl)
    {
        if (targetControl == null) return -1;

        var pos = e.GetPosition(targetControl);
        if (this.FindControl<ListBox>("ListBoxRows") is not ListBox rowsList)
            return -1;

        for (int i = 0; i < ViewModel.FloatingTriggerRows.Count; i++)
        {
            if (rowsList.ContainerFromIndex(i) is ListBoxItem lbi)
            {
                // 统一用 targetControl 作为坐标参考，避免混合参考系
                var transform = lbi.TransformToVisual(targetControl);
                if (transform == null) continue;
                var itemPos = transform.Value.Transform(new Point(0, 0));
                if (pos.Y >= itemPos.Y && pos.Y <= itemPos.Y + lbi.Bounds.Height)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    // ===== 行内按钮拖放处理 =====

    private void OnInnerButtonDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("FloatingWindowButtonId"))
        {
            // 有 FloatingWindowButtonSource 表示从行内拖来（移动），没有表示从组件库拖入（复制）
            e.DragEffects = e.Data.Contains("FloatingWindowButtonSource")
                ? DragDropEffects.Move
                : DragDropEffects.Copy;
            e.Handled = true;
        }
        else if (e.Data.Contains("FloatingWindowRow"))
        {
            // 行拖拽时不阻断（不设置 Handled，让外层 Border 处理），但也不要拒绝
            e.DragEffects = DragDropEffects.Move;
            // 注意：不设置 e.Handled = true，让事件冒泡到外层 Border
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnInnerButtonDrop(object? sender, DragEventArgs e)
    {
        // 行拖拽被内层 ListBox 拦截时，手动转发到外层 RowDropBorder 处理
        if (e.Data.Contains("FloatingWindowRow"))
        {
            // 不处理行拖拽，让外层 Border 的 Drop 处理（这里不设置 Handled，
            // 但 Avalonia 的 DragDrop 仅发送给最顶层 AllowDrop 控件，
            // 所以我们需要手动触发 RowDropBorder 的 drop 逻辑）
            HandleRowDrop(sender, e);
            return;
        }

        if (!e.Data.Contains("FloatingWindowButtonId")) return;

        var buttonId = e.Data.Get("FloatingWindowButtonId") as string;
        if (string.IsNullOrEmpty(buttonId)) return;

        var sourceCollection = e.Data.Get("FloatingWindowButtonSource") as ObservableCollection<FloatingTriggerItem>;

        // 通过 DataContext 获取目标行
        if (sender is not Control targetControl) return;
        var targetRow = targetControl.DataContext as FloatingTriggerRow;
        if (targetRow == null) return;

        var targetRowIndex = ViewModel.FloatingTriggerRows.IndexOf(targetRow);
        if (targetRowIndex < 0) return;

        // 尝试确定精确的插入位置（基于每个项的中心点判断：左半区插入前方，右半区继续下一项）
        var targetIndex = targetRow.Buttons.Count;
        if (sender is ListBox listBox)
        {
            var pos = e.GetPosition(listBox);
            for (int i = 0; i < targetRow.Buttons.Count; i++)
            {
                if (listBox.ContainerFromIndex(i) is ListBoxItem lbi)
                {
                    var transform = lbi.TransformToVisual(listBox);
                    if (transform == null) continue;
                    var itemPos = transform.Value.Transform(new Point(0, 0));
                    var center = itemPos.X + lbi.Bounds.Width / 2;
                    if (pos.X < center)
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }
        }

        e.Handled = true;

        if (sourceCollection == null)
        {
            // 从组件库拖入
            ViewModel.AddTriggerFromPool(buttonId, targetRowIndex, targetIndex);
        }
        else if (!ReferenceEquals(sourceCollection, targetRow.Buttons))
        {
            // 跨行移动
            ViewModel.MoveFloatingTrigger(buttonId, targetRowIndex, targetIndex);
        }
        else
        {
            // 行内排序
            var item = targetRow.Buttons.FirstOrDefault(b => b.ButtonId == buttonId);
            if (item == null) return;
            var sourceIndex = targetRow.Buttons.IndexOf(item);
            if (sourceIndex < 0 || sourceIndex == targetIndex) return;

            targetRow.Buttons.Move(sourceIndex, System.Math.Clamp(targetIndex, 0, targetRow.Buttons.Count - 1));
            ViewModel.PersistFloatingTriggerRows();
        }
    }

    /// <summary>
    /// 行拖拽被内层按钮 ListBox 拦截时，复用 RowDropBorder 的处理逻辑
    /// </summary>
    private void HandleRowDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.Contains("FloatingWindowRow")) return;

        var sourceRow = e.Data.Get("FloatingWindowRow") as FloatingTriggerRow;
        if (sourceRow == null) return;

        // 基于当前控件与行列表，确定目标行索引
        if (sender is not Control ctrl) return;
        var rowDropBorder = this.FindControl<Border>("RowDropBorder");
        if (rowDropBorder == null) return;
        var rowTargetIndex = FindTargetRowIndex(e, rowDropBorder);
        if (rowTargetIndex < 0) return;

        var sourceIndex = ViewModel.FloatingTriggerRows.IndexOf(sourceRow);
        if (sourceIndex < 0 || sourceIndex == rowTargetIndex) return;

        ViewModel.FloatingTriggerRows.RemoveAt(sourceIndex);
        if (rowTargetIndex > sourceIndex) rowTargetIndex--;
        ViewModel.FloatingTriggerRows.Insert(rowTargetIndex, sourceRow);

        for (int i = 0; i < ViewModel.FloatingTriggerRows.Count; i++)
        {
            ViewModel.FloatingTriggerRows[i].RowIndex = i + 1;
        }

        ViewModel.PersistFloatingTriggerRows();
    }
}

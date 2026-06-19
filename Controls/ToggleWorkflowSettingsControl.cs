using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using SystemTools.Settings;
using ClassIsland.Shared;
using Workflow = ClassIsland.Core.Models.Automation.Workflow;

namespace SystemTools.Controls;

/// <summary>
/// 切换自动化启用状态的设置控件
/// </summary>
public class ToggleWorkflowSettingsControl : ActionSettingsControlBase<ToggleWorkflowSettings>
{
    private ComboBox _workflowComboBox;
    private ComboBox _modeComboBox;
    private CheckBox _revertCheckBox;
    private ObservableCollection<Workflow> _workflows = [];
    private TextBlock _infoTextBlock;

    public ToggleWorkflowSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        // 标题
        panel.Children.Add(new TextBlock
        {
            Text = "开启/关闭选中的自动化方案",
            FontWeight = FontWeight.Bold,
            FontSize = 14
        });

        // 说明文字
        panel.Children.Add(new TextBlock
        {
            Text = "选择一个自动化方案并设置要执行的操作。当触发器支持恢复时，可以自动还原状态。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8
        });

        // 自动化选择
        panel.Children.Add(new TextBlock
        {
            Text = "目标自动化:",
            Margin = new(0, 10, 0, 0)
        });

        _workflowComboBox = new ComboBox
        {
            PlaceholderText = "请选择自动化方案",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(_workflowComboBox);

        // 操作模式选择
        panel.Children.Add(new TextBlock
        {
            Text = "操作模式:",
            Margin = new(0, 10, 0, 0)
        });

        _modeComboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _modeComboBox.Items.Add(new ComboBoxItem { Content = "切换（启用↔禁用）", Tag = null });
        _modeComboBox.Items.Add(new ComboBoxItem { Content = "启用", Tag = true });
        _modeComboBox.Items.Add(new ComboBoxItem { Content = "禁用", Tag = false });
        _modeComboBox.SelectedIndex = 0;
        panel.Children.Add(_modeComboBox);

        // 恢复选项
        _revertCheckBox = new CheckBox
        {
            Content = "触发器恢复时自动还原原状态",
            IsChecked = true,
            Margin = new(0, 10, 0, 0)
        };
        panel.Children.Add(_revertCheckBox);

        // 恢复说明
        panel.Children.Add(new TextBlock
        {
            Text = "提示：当触发器支持恢复（如\"上课时\"在放学时恢复），勾选此项会自动将自动化恢复到触发前的状态。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
            Margin = new(0, 0, 0, 0)
        });

        // 信息提示区域
        _infoTextBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 10, 0, 0),
            IsVisible = false
        };
        panel.Children.Add(_infoTextBlock);

        // 刷新按钮
        var refreshButton = new Button
        {
            Content = "刷新自动化列表",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(0, 10, 0, 0)
        };
        refreshButton.Click += (_, _) => LoadWorkflows();
        panel.Children.Add(refreshButton);

        Content = panel;

        // 加载自动化列表
        LoadWorkflows();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // 绑定选择变更事件
        _workflowComboBox.SelectionChanged += OnWorkflowSelectionChanged;
        _modeComboBox.SelectionChanged += OnModeSelectionChanged;
        _revertCheckBox.IsCheckedChanged += OnRevertCheckBoxChanged;

        // 恢复设置值
        RestoreSettings();
    }

    private void LoadWorkflows()
    {
        try
        {
            var automationService = IAppHost.TryGetService<IAutomationService>();
            if (automationService?.Workflows == null)
            {
                _infoTextBlock.Text = "无法获取自动化服务，请确保 ClassIsland 已正确加载。";
                _infoTextBlock.Foreground = Brushes.Orange;
                _infoTextBlock.IsVisible = true;
                return;
            }

            _workflows = automationService.Workflows;
            _workflowComboBox.Items.Clear();

            foreach (var workflow in _workflows)
            {
                var actionSet = workflow.ActionSet;
                var statusText = actionSet.IsEnabled ? "[已启用]" : "[已禁用]";
                var item = new ComboBoxItem
                {
                    Content = $"{actionSet.Name} {statusText}",
                    Tag = workflow
                };
                _workflowComboBox.Items.Add(item);
            }

            if (_workflowComboBox.Items.Count == 0)
            {
                _workflowComboBox.PlaceholderText = "暂无自动化方案";
                _infoTextBlock.Text = "当前配置文件中没有任何自动化方案，请先创建自动化。";
                _infoTextBlock.Foreground = Brushes.Gray;
                _infoTextBlock.IsVisible = true;
            }
            else
            {
                _infoTextBlock.IsVisible = false;
            }

            // 恢复之前的选择
            RestoreSettings();
        }
        catch (Exception ex)
        {
            _infoTextBlock.Text = $"加载自动化列表失败: {ex.Message}";
            _infoTextBlock.Foreground = Brushes.Red;
            _infoTextBlock.IsVisible = true;
        }
    }

    private void RestoreSettings()
    {
        if (Settings == null) return;

        // 恢复自动化选择
        if (Settings.TargetWorkflowIndex >= 0 && Settings.TargetWorkflowIndex < _workflowComboBox.Items.Count)
        {
            _workflowComboBox.SelectedIndex = Settings.TargetWorkflowIndex;
        }
        else if (!string.IsNullOrEmpty(Settings.TargetWorkflowName))
        {
            // 尝试通过名称查找
            for (int i = 0; i < _workflowComboBox.Items.Count; i++)
            {
                if (_workflowComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag is Workflow workflow &&
                    workflow.ActionSet.Name == Settings.TargetWorkflowName)
                {
                    _workflowComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        // 恢复操作模式
        var modeIndex = Settings.EnableMode switch
        {
            null => 0,  // 切换
            true => 1,  // 启用
            false => 2  // 禁用
        };
        _modeComboBox.SelectedIndex = modeIndex;

        // 恢复复选框
        _revertCheckBox.IsChecked = Settings.RevertToOriginal;
    }

    private void OnWorkflowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_workflowComboBox.SelectedItem is ComboBoxItem item && item.Tag is Workflow workflow)
        {
            Settings.TargetWorkflowName = workflow.ActionSet.Name;
            Settings.TargetWorkflowIndex = _workflowComboBox.SelectedIndex;

            // 更新信息显示
            var status = workflow.ActionSet.IsEnabled ? "已启用" : "已禁用";
            _infoTextBlock.Text = $"当前状态: {status} | 行动组: {workflow.ActionSet.Name}";
            _infoTextBlock.Foreground = workflow.ActionSet.IsEnabled ? Brushes.Green : Brushes.Gray;
            _infoTextBlock.IsVisible = true;
        }
    }

    private void OnModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_modeComboBox.SelectedItem is ComboBoxItem item && item.Tag is bool mode)
        {
            Settings.EnableMode = mode;
        }
        else
        {
            Settings.EnableMode = null; // 切换模式
        }
    }

    private void OnRevertCheckBoxChanged(object? sender, EventArgs e)
    {
        if (_revertCheckBox.IsChecked.HasValue)
        {
            Settings.RevertToOriginal = _revertCheckBox.IsChecked.Value;
        }
    }
}

using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;
using SystemTools.Services;
using ClassIsland.Shared;

namespace SystemTools.Controls;

/// <summary>
/// 切换悬浮窗配置方案行动的设置控件
/// </summary>
public class ToggleFloatingWindowProfileSettingsControl : ActionSettingsControlBase<ToggleFloatingWindowProfileSettings>
{
    private ComboBox _profileComboBox;
    private CheckBox _notifyCheckBox;

    public ToggleFloatingWindowProfileSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "目标配置方案:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _profileComboBox = new ComboBox
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        LoadProfiles();

        panel.Children.Add(_profileComboBox);

        panel.Children.Add(new TextBlock
        {
            Text = "提示：选择\"切换到下一个\"会按顺序循环切换方案，选择具体方案会直接切换到该方案。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12
        });        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        

        Content = panel;
    }

    private void LoadProfiles()
    {
        _profileComboBox.Items.Clear();
        _profileComboBox.Items.Add(new ComboBoxItem { Content = "切换到下一个", Tag = null });

        try
        {
            var profileManager = IAppHost.GetService<FloatingWindowService>().ProfileManager;
            var profileNames = profileManager.GetProfileNames();

            foreach (var name in profileNames)
            {
                _profileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = name,
                    Tag = name
                });
            }
        }
        catch
        {
            // 服务可能尚未初始化
        }

        _profileComboBox.SelectedIndex = 0;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;

        _profileComboBox.SelectionChanged += OnProfileSelectionChanged;

        RestoreSettings();
    }

    private void RestoreSettings()
    {
        if (Settings == null) return;

        var targetName = Settings.TargetProfileName;
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            for (int i = 1; i < _profileComboBox.Items.Count; i++)
            {
                if (_profileComboBox.Items[i] is ComboBoxItem item && item.Tag is string name && name == targetName)
                {
                    _profileComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        _profileComboBox.SelectedIndex = 0;
    }

    private void OnProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_profileComboBox.SelectedItem is ComboBoxItem item)
        {
            Settings.TargetProfileName = item.Tag as string;
        }
    }
}

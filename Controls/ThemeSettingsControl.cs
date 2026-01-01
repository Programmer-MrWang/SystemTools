using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ThemeSettingsControl : ActionSettingsControlBase<ThemeSettings>
{
    private readonly string _filePath;
    private ComboBox _themeComboBox;

    public ThemeSettingsControl()
    {
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "themes.json");

        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "切换主题色",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var comboPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };

        comboPanel.Children.Add(new TextBlock
        {
            Text = "选择主题:",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        _themeComboBox = new ComboBox
        {
            Width = 100,
            ItemsSource = new[] { "浅色", "深色" },
            SelectedIndex = 0
        };

        LoadExistingTheme();
        _themeComboBox.SelectionChanged += async (s, e) => await SaveThemeAsync();

        comboPanel.Children.Add(_themeComboBox);
        panel.Children.Add(comboPanel);

        Content = panel;
    }

    private void LoadExistingTheme()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
                if (settings != null)
                {
                    _themeComboBox.SelectedItem = settings.Theme;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载 themes.json 失败: {ex.Message}");
        }
    }

    private async Task SaveThemeAsync()
    {
        try
        {
            var settings = new ThemeSettings
            {
                Theme = _themeComboBox.SelectedItem?.ToString() ?? "浅色"
            };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存 themes.json 失败: {ex.Message}");
        }
    }
}
using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ShutdownSettingsControl : ActionSettingsControlBase<ShutdownSettings>
{
    private readonly string _filePath;
    private NumericUpDown _secondsInput;
    private CheckBox _promptCheckBox;

    public ShutdownSettingsControl()
    {
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "shutdown.json");

        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "计时关机设置",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var secondsPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 5
        };

        secondsPanel.Children.Add(new TextBlock
        {
            Text = "关机倒计时（秒）:",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        _secondsInput = new NumericUpDown
        {
            Width = 100,
            Minimum = 0,
            Maximum = 86400,
            Value = 60,
            Increment = 10
        };
        _secondsInput.ValueChanged += async (s, e) => await SaveSettingsAsync();

        secondsPanel.Children.Add(_secondsInput);
        panel.Children.Add(secondsPanel);

        _promptCheckBox = new CheckBox
        {
            Content = "不显示提示",
            IsChecked = false,
            Margin = new Avalonia.Thickness(0, 5, 0, 0)
        };
        _promptCheckBox.IsCheckedChanged += async (s, e) => await SaveSettingsAsync();

        panel.Children.Add(_promptCheckBox);

        LoadExistingSettings();
        Content = panel;
    }

    private void LoadExistingSettings()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<ShutdownSettings>(json);
                if (settings != null)
                {
                    _secondsInput.Value = settings.Seconds;
                    _promptCheckBox.IsChecked = !settings.ShowPrompt;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载 shutdown.json 失败: {ex.Message}");
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new ShutdownSettings
            {
                Seconds = (int)(_secondsInput.Value ?? 60),
                ShowPrompt = !(_promptCheckBox.IsChecked ?? false)
            };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存 shutdown.json 失败: {ex.Message}");
        }
    }
}
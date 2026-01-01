using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class TypeContentSettingsControl : ActionSettingsControlBase<TypeContentSettings>
{
    private readonly string _filePath;
    private TextBox _textBox;

    public TypeContentSettingsControl()
    {
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "type.json");

        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "要键入的内容:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _textBox = new TextBox
        {
            Watermark = "输入要粘贴的文本内容",
            Text = LoadExistingContent(),
            AcceptsReturn = true,
            Height = 100
        };

        _textBox.TextChanged += async (s, e) => await SaveContentAsync();

        panel.Children.Add(_textBox);
        Content = panel;
    }

    private string LoadExistingContent()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<TypeContentSettings>(json);
                return settings?.Content ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载 type.json 失败: {ex.Message}");
        }
        return string.Empty;
    }

    private async Task SaveContentAsync()
    {
        try
        {
            var settings = new TypeContentSettings { Content = _textBox.Text ?? string.Empty };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存 type.json 失败: {ex.Message}");
        }
    }
    private void OnSettingsChanged() { }
}
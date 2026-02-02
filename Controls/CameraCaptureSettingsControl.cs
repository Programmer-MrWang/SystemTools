using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class CameraCaptureSettingsControl : ActionSettingsControlBase<CameraCaptureSettings>
{
    private TextBox _deviceNameBox;
    private TextBox _pathBox;

    public CameraCaptureSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "摄像头设备名:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _deviceNameBox = new TextBox
        {
            Watermark = "输入摄像头名（在系统 设备管理器 中查询）"
        };
        _deviceNameBox.TextChanged += (s, e) => Settings.DeviceName = _deviceNameBox.Text ?? "";
        panel.Children.Add(_deviceNameBox);

        panel.Children.Add(new TextBlock
        {
            Text = "保存路径:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _pathBox = new TextBox
        {
            Watermark = "输入图片保存路径：不允许中文字符"
        };
        _pathBox.TextChanged += (s, e) => Settings.SavePath = _pathBox.Text ?? "";
        panel.Children.Add(_pathBox);

        var browseButton = new Button
        {
            Content = "浏览...",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Margin = new(0, 5, 0, 0)
        };
        browseButton.Click += async (sender, e) => await BrowseButton_Click();
        panel.Children.Add(browseButton);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _deviceNameBox.Text = Settings.DeviceName;
        _pathBox.Text = Settings.SavePath;
    }

    private async Task BrowseButton_Click()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                var logger = IAppHost.TryGetService<ILogger<CameraCaptureSettingsControl>>();
                logger?.LogWarning("无法获取 TopLevel");
                return;
            }

            var options = new FilePickerSaveOptions
            {
                Title = "选择图片保存位置",
                DefaultExtension = "png",
                SuggestedFileName = $"{DateTime.Now:yyyyMMdd_HHmmss}.png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG 图片")
                    {
                        Patterns = new[] { "*.png" }
                    }
                }
            };

            var result = await topLevel.StorageProvider.SaveFilePickerAsync(options);
            if (result != null)
            {
                Settings.SavePath = result.Path.LocalPath;
                _pathBox.Text = result.Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            var logger = IAppHost.TryGetService<ILogger<CameraCaptureSettingsControl>>();
            logger?.LogError(ex, "选择保存路径失败");
        }
    }
}
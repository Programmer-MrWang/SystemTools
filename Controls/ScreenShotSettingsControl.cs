using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ScreenShotSettingsControl : ActionSettingsControlBase<ScreenShotSettings>
{
    private TextBox _pathBox;

    public ScreenShotSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "屏幕截图",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var pathPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };

        _pathBox = new TextBox
        {
            Watermark = "选择保存路径",
            Width = 300
        };
        _pathBox.TextChanged += (s, e) => Settings.SavePath = _pathBox.Text ?? "";
        pathPanel.Children.Add(_pathBox);

        var browseButton = new Avalonia.Controls.Button
        {
            Content = "浏览...",
            Width = 80
        };
        browseButton.Click += async (s, e) => await BrowseButton_Click();
        pathPanel.Children.Add(browseButton);

        panel.Children.Add(pathPanel);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _pathBox.Text = Settings.SavePath;
    }

    private async Task BrowseButton_Click()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                var logger = IAppHost.TryGetService<ILogger<ScreenShotSettingsControl>>();
                logger?.LogWarning("无法获取 TopLevel");
                return;
            }

            var options = new FilePickerSaveOptions
            {
                Title = "保存屏幕截图",
                DefaultExtension = "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG图片")
                    {
                        Patterns = new[] { "*.png" }
                    },
                    new FilePickerFileType("JPEG图片")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg" }
                    },
                    new FilePickerFileType("所有文件")
                    {
                        Patterns = new[] { "*" }
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
            var logger = IAppHost.TryGetService<ILogger<ScreenShotSettingsControl>>();
            logger?.LogError(ex, "选择保存路径失败");
        }
    }
}
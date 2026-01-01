using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage; 
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ChangeWallpaperSettingsControl : ActionSettingsControlBase<ChangeWallpaperSettings>
{
    //public ChangeWallpaperSettings Settings => (ChangeWallpaperSettings)SettingsInternal;

    public ChangeWallpaperSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "图片路径:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        var pathBox = new TextBox
        {
            Watermark = "输入路径：路径中不得出现中文字符",
            [!TextBox.TextProperty] = new Binding(nameof(Settings.ImagePath))
        };
        panel.Children.Add(pathBox);

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

    private async Task BrowseButton_Click()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                var logger = IAppHost.TryGetService<ILogger<ChangeWallpaperSettingsControl>>();
                logger?.LogWarning("无法获取 TopLevel");
                return;
            }

            var options = new FilePickerOpenOptions
            {
                Title = "选择壁纸图片",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" }
                    },
                    new FilePickerFileType("所有文件")
                    {
                        Patterns = new[] { "*" }
                    }
                }
            };

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            if (result?.Count > 0)
            {
                Settings.ImagePath = result[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            var logger = IAppHost.TryGetService<ILogger<ChangeWallpaperSettingsControl>>();
            logger?.LogError(ex, "选择壁纸文件失败");
        }
    }
}
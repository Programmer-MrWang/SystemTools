using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ChangeWallpaperSettingsControl : ActionSettingsControlBase<ChangeWallpaperSettings>
{
    private Avalonia.Controls.TextBox _pathBox;
    private Avalonia.Controls.Button _browseButton;

    public ChangeWallpaperSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "图片路径:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _pathBox = new Avalonia.Controls.TextBox
        {
            Watermark = "请选择壁纸图片文件"
        };
        _pathBox.TextChanged += (s, e) =>
        {
            Settings.ImagePath = _pathBox.Text ?? "";
        };
        panel.Children.Add(_pathBox);

        _browseButton = new Avalonia.Controls.Button
        {
            Content = "浏览...",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Margin = new(0, 5, 0, 0)
        };
        _browseButton.Click += async (sender, e) => await BrowseButton_Click();
        panel.Children.Add(_browseButton);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _pathBox.Text = Settings.ImagePath;
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
                var path = result[0].Path.LocalPath;
                Settings.ImagePath = path;
                _pathBox.Text = path;
            }
        }
        catch (Exception ex)
        {
            var logger = IAppHost.TryGetService<ILogger<ChangeWallpaperSettingsControl>>();
            logger?.LogError(ex, "选择壁纸文件失败");
        }
    }
}
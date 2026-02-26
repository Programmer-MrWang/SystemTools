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
    private TextBox _folderPathBox;

    public ScreenShotSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "屏幕截图",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var pathPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };

        _folderPathBox = new TextBox
        {
            Watermark = "点击\"浏览...\"以选择保存文件夹",
            Width = 300,
            IsReadOnly = true
        };
        pathPanel.Children.Add(_folderPathBox);

        var browseButton = new Button
        {
            Content = "浏览...",
            Width = 80
        };
        browseButton.Click += async (s, e) => await BrowseFolder_Click();
        pathPanel.Children.Add(browseButton);

        panel.Children.Add(pathPanel);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _folderPathBox.Text = Settings.SaveFolder;
    }

    private async Task BrowseFolder_Click()
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

            var options = new FolderPickerOpenOptions
            {
                Title = "选择截图保存文件夹",
                AllowMultiple = false
            };

            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
            if (result != null && result.Count > 0)
            {
                Settings.SaveFolder = result[0].Path.LocalPath;
                _folderPathBox.Text = Settings.SaveFolder;
            }
        }
        catch (Exception ex)
        {
            var logger = IAppHost.TryGetService<ILogger<ScreenShotSettingsControl>>();
            logger?.LogError(ex, "选择保存文件夹失败");
        }
    }
}
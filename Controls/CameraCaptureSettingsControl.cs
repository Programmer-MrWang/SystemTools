using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class CameraCaptureSettingsControl : ActionSettingsControlBase<CameraCaptureSettings>
{
    private TextBox _deviceNameBox;
    private TextBox _folderPathBox;

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
        panel.Children.Add(_deviceNameBox);

        panel.Children.Add(new TextBlock
        {
            Text = "保存文件夹:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _folderPathBox = new TextBox
        {
            Watermark = "点击\"浏览...\"以选择保存文件夹",
            IsReadOnly = true
        };
        panel.Children.Add(_folderPathBox);

        var browseButton = new Button
        {
            Content = "浏览...",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Margin = new(0, 5, 0, 0)
        };
        browseButton.Click += async (sender, e) => await BrowseFolder_Click();
        panel.Children.Add(browseButton);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        
        _deviceNameBox.Bind(
            TextBox.TextProperty, 
            new Avalonia.Data.Binding(nameof(Settings.DeviceName)) 
            { 
                Source = Settings,
                Mode = Avalonia.Data.BindingMode.TwoWay
            });
        
        _folderPathBox.Bind(
            TextBox.TextProperty,
            new Avalonia.Data.Binding(nameof(Settings.SaveFolder))
            {
                Source = Settings,
                Mode = Avalonia.Data.BindingMode.TwoWay
            });
    }

    private async Task BrowseFolder_Click()
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

            var options = new FolderPickerOpenOptions
            {
                Title = "选择图片保存文件夹",
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
            var logger = IAppHost.TryGetService<ILogger<CameraCaptureSettingsControl>>();
            logger?.LogError(ex, "选择保存文件夹失败");
        }
    }
}
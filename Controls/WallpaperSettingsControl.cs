using Avalonia.Controls;
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
    private Avalonia.Controls.ComboBox _modeComboBox;
    private Avalonia.Controls.TextBlock _pathLabel;
    private Avalonia.Controls.TextBox _pathBox;
    private Avalonia.Controls.Button _browseButton;
    private Avalonia.Controls.TextBlock _fitLabel;
    private Avalonia.Controls.ComboBox _fitComboBox;
    private Avalonia.Controls.TextBlock _solidColorLabel;
    private Avalonia.Controls.TextBox _solidColorBox;

    public ChangeWallpaperSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "壁纸类型:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _modeComboBox = new Avalonia.Controls.ComboBox
        {
            ItemsSource = new[] { "图片壁纸", "纯色壁纸" },
            Width = 200
        };
        _modeComboBox.SelectionChanged += (s, e) =>
        {
            if (_modeComboBox.SelectedIndex >= 0)
            {
                Settings.Mode = (ChangeWallpaperMode)_modeComboBox.SelectedIndex;
                RefreshModeVisibility();
            }
        };
        panel.Children.Add(_modeComboBox);

        _pathLabel = new Avalonia.Controls.TextBlock
        {
            Text = "图片路径:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        };
        panel.Children.Add(_pathLabel);

        _pathBox = new Avalonia.Controls.TextBox
        {
            Watermark = "请选择壁纸图片文件"
        };
        _pathBox.TextChanged += (s, e) => { Settings.ImagePath = _pathBox.Text ?? ""; };
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

        // 新增：契合度下拉
        _fitLabel = new Avalonia.Controls.TextBlock
        {
            Text = "壁纸契合度:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(_fitLabel);

        _fitComboBox = new Avalonia.Controls.ComboBox
        {
            ItemsSource = new[] { "平铺", "居中", "拉伸", "填充", "适应", "跨区" },
            Width = 200
        };
        _fitComboBox.SelectionChanged += (s, e) =>
        {
            if (_fitComboBox.SelectedIndex >= 0)
                Settings.FitStyle = _fitComboBox.SelectedIndex;
        };
        panel.Children.Add(_fitComboBox);

        _solidColorLabel = new Avalonia.Controls.TextBlock
        {
            Text = "纯色颜色:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(_solidColorLabel);

        _solidColorBox = new Avalonia.Controls.TextBox
        {
            Watermark = "#000000 或 0,0,0",
            Width = 200,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        _solidColorBox.TextChanged += (s, e) => { Settings.SolidColor = _solidColorBox.Text ?? "#000000"; };
        panel.Children.Add(_solidColorBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _modeComboBox.SelectedIndex = (int)Settings.Mode;
        _pathBox.Text = Settings.ImagePath;
        _fitComboBox.SelectedIndex = Settings.FitStyle;
        _solidColorBox.Text = Settings.SolidColor;
        RefreshModeVisibility();
    }

    private void RefreshModeVisibility()
    {
        var isImageMode = Settings.Mode == ChangeWallpaperMode.Image;
        _pathLabel.IsVisible = isImageMode;
        _pathBox.IsVisible = isImageMode;
        _browseButton.IsVisible = isImageMode;
        _fitLabel.IsVisible = isImageMode;
        _fitComboBox.IsVisible = isImageMode;
        _solidColorLabel.IsVisible = !isImageMode;
        _solidColorBox.IsVisible = !isImageMode;
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
                        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.tiff", "*.webp" }
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

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class MoveSettingsControl : ActionSettingsControlBase<MoveSettings>
{
    private Avalonia.Controls.ComboBox _typeComboBox;
    private Avalonia.Controls.TextBox _sourcePathBox;
    private Avalonia.Controls.TextBox _destinationPathBox;

    public MoveSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "移动文件或文件夹",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var typePanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };
        typePanel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "操作类型:",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        _typeComboBox = new Avalonia.Controls.ComboBox
        {
            Width = 100,
            ItemsSource = new[] { "文件", "文件夹" }
        };
        _typeComboBox.SelectionChanged += (s, e) =>
        {
            Settings.OperationType = _typeComboBox.SelectedItem?.ToString() ?? "文件";
        };
        typePanel.Children.Add(_typeComboBox);
        panel.Children.Add(typePanel);

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "从:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new(0, 10, 0, 0)
        });
        _sourcePathBox = new Avalonia.Controls.TextBox
        {
            Watermark = "源文件/文件夹路径"
        };
        _sourcePathBox.TextChanged += (s, e) => Settings.SourcePath = _sourcePathBox.Text ?? "";
        panel.Children.Add(_sourcePathBox);
        var sourceBrowseButton = new Avalonia.Controls.Button
        {
            Content = "浏览...",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        sourceBrowseButton.Click += async (s, e) => await BrowsePath(true);
        panel.Children.Add(sourceBrowseButton);

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "到:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new(0, 10, 0, 0)
        });
        _destinationPathBox = new Avalonia.Controls.TextBox
        {
            Watermark = "目标文件夹路径"
        };
        _destinationPathBox.TextChanged += (s, e) => Settings.DestinationPath = _destinationPathBox.Text ?? "";
        panel.Children.Add(_destinationPathBox);
        var destBrowseButton = new Avalonia.Controls.Button
        {
            Content = "浏览...",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        destBrowseButton.Click += async (s, e) => await BrowsePath(false);
        panel.Children.Add(destBrowseButton);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _typeComboBox.SelectedItem = Settings.OperationType;
        _sourcePathBox.Text = Settings.SourcePath;
        _destinationPathBox.Text = Settings.DestinationPath;
    }

    private async Task BrowsePath(bool isSource)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var isFolder = isSource ? Settings.OperationType == "文件夹" : true;

        if (isFolder)
        {
            var options = new FolderPickerOpenOptions
            {
                Title = isSource ? "选择源文件夹" : "选择目标文件夹"
            };
            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
            if (result?.Count > 0)
            {
                var uri = result[0].Path;
                var path = uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
                if (isSource)
                {
                    Settings.SourcePath = path;
                    _sourcePathBox.Text = path;
                }
                else
                {
                    Settings.DestinationPath = path;
                    _destinationPathBox.Text = path;
                }
            }
        }
        else
        {
            var options = new FilePickerOpenOptions
            {
                Title = "选择源文件",
                FileTypeFilter = new[] { new FilePickerFileType("所有文件") { Patterns = new[] { "*" } } }
            };
            var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            if (result?.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                Settings.SourcePath = path;
                _sourcePathBox.Text = path;
            }
        }
    }
}
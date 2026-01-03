using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class DeleteSettingsControl : ActionSettingsControlBase<DeleteSettings>
{
    private Avalonia.Controls.ComboBox _typeComboBox;
    private Avalonia.Controls.TextBox _targetPathBox;

    public DeleteSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "删除文件或文件夹",
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
            Text = "目标路径:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new(0, 10, 0, 0)
        });
        _targetPathBox = new Avalonia.Controls.TextBox
        {
            Watermark = "要删除的文件/文件夹路径"
        };
        _targetPathBox.TextChanged += (s, e) => Settings.TargetPath = _targetPathBox.Text ?? "";
        panel.Children.Add(_targetPathBox);
        var browseButton = new Avalonia.Controls.Button
        {
            Content = "浏览...",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        browseButton.Click += async (s, e) => await BrowsePath();
        panel.Children.Add(browseButton);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _typeComboBox.SelectedItem = Settings.OperationType;
        _targetPathBox.Text = Settings.TargetPath;
    }

    private async Task BrowsePath()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var isFolder = Settings.OperationType == "文件夹";

        if (isFolder)
        {
            var options = new FolderPickerOpenOptions
            {
                Title = "选择要删除的文件夹"
            };
            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
            if (result?.Count > 0)
            {
                var uri = result[0].Path;
                var path = uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
                Settings.TargetPath = path;
                _targetPathBox.Text = path;
            }
        }
        else
        {
            var options = new FilePickerOpenOptions
            {
                Title = "选择要删除的文件",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            };
            var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            if (result?.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                Settings.TargetPath = path;
                _targetPathBox.Text = path;
            }
        }
    }
}
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using System;
using SystemTools.Models.ComponentSettings;

namespace SystemTools.Controls.Components;

public partial class LocalQuoteSettingsControl : ComponentBase<LocalQuoteSettings>
{
    public Array ProgressBarPositions { get; } = Enum.GetValues(typeof(LocalQuoteProgressBarPosition));

    public LocalQuoteSettingsControl()
    {
        InitializeComponent();
    }

    private async void BrowseQuotesFileButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择本地一言 txt 文件",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("文本文件") { Patterns = ["*.txt"] },
                    new FilePickerFileType("所有文件") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0)
            {
                Settings.QuotesFilePath = files[0].Path.LocalPath;
            }
        }
        catch
        {
        }
    }
}

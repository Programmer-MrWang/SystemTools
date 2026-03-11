using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Controls;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using SystemTools.Triggers;

namespace SystemTools.Settings;

public class FloatingWindowTriggerSettings : TriggerSettingsControlBase<FloatingWindowTriggerConfig>
{
    private const int IconCodeStart = 0xE000;
    private const int IconCodeEnd = 0xF4D3;

    private readonly TextBox _iconTextBox;
    private readonly TextBox _nameTextBox;
    private readonly List<string> _iconTokens = new();

    private ContentDialog? _iconPickerDialog;

    public FloatingWindowTriggerSettings()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "悬浮窗按钮图标（/uXXXX）",
            TextWrapping = TextWrapping.Wrap
        });

        _iconTextBox = new TextBox { Watermark = "/uEA73", HorizontalAlignment = HorizontalAlignment.Stretch };
        _iconTextBox.TextChanged += (_, _) => { Settings.Icon = _iconTextBox.Text ?? string.Empty; };

        var iconRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        iconRow.Children.Add(_iconTextBox);

        var pickIconButton = new Button
        {
            Content = "选择图标",
            VerticalAlignment = VerticalAlignment.Center
        };
        pickIconButton.Click += async (_, _) => await OpenIconPickerDialogAsync();
        Grid.SetColumn(pickIconButton, 1);
        iconRow.Children.Add(pickIconButton);
        panel.Children.Add(iconRow);

        panel.Children.Add(new TextBlock
        {
            Text = "悬浮窗按钮名称",
            TextWrapping = TextWrapping.Wrap
        });

        _nameTextBox = new TextBox { Watermark = "例如：按钮1" };
        _nameTextBox.TextChanged += (_, _) => { Settings.ButtonName = _nameTextBox.Text ?? string.Empty; };
        panel.Children.Add(_nameTextBox);

        panel.Children.Add(new TextBlock
        {
            Text = "您可在 SystemTools 悬浮窗编辑页面中 进一步设置悬浮窗样式。\n若勾选“启用恢复”则可通过再次点按按钮实现恢复。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray
        });

        Content = panel;
    }

    private async Task OpenIconPickerDialogAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        EnsureIconsLoaded();
        var rows = BuildVirtualizedRows(460);

        _iconPickerDialog = new ContentDialog
        {
            Title = "选择悬浮窗图标",
            PrimaryButtonText = "关闭",
            DefaultButton = ContentDialogButton.Primary,
            Content = BuildIconPickerContent(rows)
        };

        await _iconPickerDialog.ShowAsync(topLevel);
        _iconPickerDialog = null;
    }

    private Control BuildIconPickerContent(ObservableCollection<IconRow> rows)
    {
        var listBox = new ListBox
        {
            ItemsSource = rows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        listBox.ItemsPanel = new FuncTemplate<Panel>(() => new VirtualizingStackPanel());
        listBox.ItemTemplate = new FuncDataTemplate<IconRow?>((row, _) => BuildIconRow(row));

        listBox.Height = 520;
        ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);

        return new Border
        {
            Padding = new Thickness(8),
            Child = listBox
        };
    }

    private Control BuildIconRow(IconRow? row)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 36,
            ItemHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };

        if (row?.Tokens == null || row.Tokens.Count == 0)
        {
            return panel;
        }

        foreach (var token in row.Tokens)
        {
            panel.Children.Add(BuildIconButton(token));
        }

        return panel;
    }

    private Control BuildIconButton(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new Border { Width = 34, Height = 34 };
        }

        var iconButton = new Button
        {
            Width = 34,
            Height = 34,
            Margin = new Thickness(1),
            //ToolTip = token,
            Padding = new Thickness(0),
            Content = new FluentIcon
            {
                Glyph = ToGlyph(token),
                FontSize = 21,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        iconButton.Click += (_, _) => SelectIcon(token);
        return iconButton;
    }

    private ObservableCollection<IconRow> BuildVirtualizedRows(double dialogWidth)
    {
        var columns = Math.Max(6, (int)((dialogWidth - 32) / 38));
        var rows = new ObservableCollection<IconRow>();

        foreach (var chunk in _iconTokens.Chunk(columns))
        {
            rows.Add(new IconRow(chunk.ToList()));
        }

        return rows;
    }

    private static string ToGlyph(string token)
    {
        if (token.Length > 2 && (token.StartsWith("/u", StringComparison.OrdinalIgnoreCase) || token.StartsWith("\\u", StringComparison.OrdinalIgnoreCase)))
        {
            if (int.TryParse(token[2..], System.Globalization.NumberStyles.HexNumber, null, out var code))
            {
                return char.ConvertFromUtf32(code);
            }
        }

        return token;
    }

    private void EnsureIconsLoaded()
    {
        if (_iconTokens.Count > 0)
        {
            return;
        }

        for (var code = IconCodeStart; code <= IconCodeEnd; code++)
        {
            _iconTokens.Add($"/u{code:X4}");
        }
    }

    private void SelectIcon(string token)
    {
        Settings.Icon = token;
        _iconTextBox.Text = token;
        _iconPickerDialog?.Hide();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _iconTextBox.Text = Settings.Icon;
        _nameTextBox.Text = Settings.ButtonName;
    }

    private sealed record IconRow(IReadOnlyList<string> Tokens);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SystemTools.Triggers;

namespace SystemTools.Settings;

public class FloatingWindowTriggerSettings : TriggerSettingsControlBase<FloatingWindowTriggerConfig>
{
    private const int IconCodeStart = 0xE000;
    private const int IconCodeEnd = 0xF4D3;
    private const int IconsPerRow = 10;

    private readonly TextBox _iconTextBox;
    private readonly TextBox _nameTextBox;
    private readonly Grid _rootGrid;
    private readonly Border _iconDrawer;
    private readonly ObservableCollection<IconRow> _iconRows = new();

    public FloatingWindowTriggerSettings()
    {
        _rootGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,420"),
            ColumnSpacing = 12
        };

        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "悬浮窗按钮图标（示例：/uE7C3）",
            TextWrapping = TextWrapping.Wrap
        });

        _iconTextBox = new TextBox { Watermark = "/uE7C3", HorizontalAlignment = HorizontalAlignment.Stretch };
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
        pickIconButton.Click += (_, _) => OpenIconDrawer();
        Grid.SetColumn(pickIconButton, 1);
        iconRow.Children.Add(pickIconButton);
        panel.Children.Add(iconRow);

        panel.Children.Add(new TextBlock
        {
            Text = "悬浮窗按钮名称（显示在图标下方）",
            TextWrapping = TextWrapping.Wrap
        });

        _nameTextBox = new TextBox { Watermark = "例如：快捷抽取" };
        _nameTextBox.TextChanged += (_, _) => { Settings.ButtonName = _nameTextBox.Text ?? string.Empty; };
        panel.Children.Add(_nameTextBox);

        panel.Children.Add(new TextBlock
        {
            Text = "每个“从悬浮窗触发”触发器会在浮窗里生成一个按钮。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray
        });

        _rootGrid.Children.Add(panel);

        _iconDrawer = BuildIconDrawer();
        Grid.SetColumn(_iconDrawer, 1);
        _rootGrid.Children.Add(_iconDrawer);

        Content = _rootGrid;
    }

    private Border BuildIconDrawer()
    {
        var title = new TextBlock
        {
            Text = "选择悬浮窗图标",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button
        {
            Content = "关闭",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => CloseIconDrawer();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        header.Children.Add(title);
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);

        var listBox = new ListBox
        {
            ItemsSource = _iconRows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        listBox.ItemsPanel = new FuncTemplate<Panel>(() => new VirtualizingStackPanel());
        listBox.ItemTemplate = new FuncDataTemplate<IconRow>((row, _) => BuildRowPanel(row));

        var drawerContent = new StackPanel
        {
            Children =
            {
                header,
                new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#22000000")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8),
                    Child = listBox
                }
            }
        };

        return new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.Parse("#111111")),
            BorderBrush = new SolidColorBrush(Color.Parse("#33444444")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = drawerContent
        };
    }

    private Control BuildRowPanel(IconRow row)
    {
        var wrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 36,
            ItemHeight = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        foreach (var icon in row.Icons)
        {
            var iconButton = new Button
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(1),
                ToolTip = icon.Token,
                Padding = new Thickness(0),
                Content = new TextBlock
                {
                    Text = icon.Glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };

            iconButton.Click += (_, _) => SelectIcon(icon.Token);
            wrapPanel.Children.Add(iconButton);
        }

        return wrapPanel;
    }

    private void OpenIconDrawer()
    {
        if (_iconRows.Count == 0)
        {
            LoadIconRows();
        }

        _iconDrawer.IsVisible = true;
    }

    private void CloseIconDrawer()
    {
        _iconDrawer.IsVisible = false;
    }

    private void SelectIcon(string token)
    {
        Settings.Icon = token;
        _iconTextBox.Text = token;
        CloseIconDrawer();
    }

    private void LoadIconRows()
    {
        var allIcons = new List<IconItem>(IconCodeEnd - IconCodeStart + 1);
        for (var code = IconCodeStart; code <= IconCodeEnd; code++)
        {
            allIcons.Add(new IconItem($"/u{code:X4}", char.ConvertFromUtf32(code)));
        }

        foreach (var chunk in allIcons.Chunk(IconsPerRow))
        {
            _iconRows.Add(new IconRow(chunk.ToList()));
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _iconTextBox.Text = Settings.Icon;
        _nameTextBox.Text = Settings.ButtonName;
    }

    private sealed record IconItem(string Token, string Glyph);

    private sealed record IconRow(IReadOnlyList<IconItem> Icons);
}

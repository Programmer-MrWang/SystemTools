using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.ComponentModel;
using System.Linq;
using SystemTools.Models.ComponentSettings;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;

namespace SystemTools.Controls.Components;

[ComponentInfo(
    "E2A41B7D-9F36-4A08-8B8D-1BA29E570F62",
    "显示剪切板内容",
    "\uE8C8",
    "剪切板内容更新时显示最新文本"
)]
public partial class ClipboardContentComponent : ComponentBase<ClipboardContentSettings>, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private string _clipboardContent = "（暂无剪切板文本内容）";
    private string? _lastClipboardText;

    public string ClipboardContent
    {
        get => _clipboardContent;
        set
        {
            _clipboardContent = value;
            OnPropertyChanged(nameof(ClipboardContent));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public ClipboardContentComponent()
    {
        InitializeComponent();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTimerTicked;
    }

    private void ClipboardContentComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _timer.Start();
        _ = RefreshClipboardAsync();
    }

    private void ClipboardContentComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _timer.Stop();
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        _ = RefreshClipboardAsync();
    }

    private static string NormalizeToSingleLine(string text)
    {
        var singleLine = string.Join(" ",
            text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(singleLine)
            ? "（剪切板为空或当前内容不是文本）"
            : singleLine;
    }

    private async System.Threading.Tasks.Task RefreshClipboardAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            var text = await clipboard.GetTextAsync();
            if (text == _lastClipboardText)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _lastClipboardText = text;
                ClipboardContent = "（剪切板为空或当前内容不是文本）";
                return;
            }

            _lastClipboardText = text;
            ClipboardContent = NormalizeToSingleLine(text);
        }
        catch
        {
            ClipboardContent = "（读取剪切板失败）";
        }
    }
}

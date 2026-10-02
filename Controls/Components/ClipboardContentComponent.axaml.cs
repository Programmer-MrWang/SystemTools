using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using SystemTools.Models.ComponentSettings;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;

namespace SystemTools.Controls.Components;

[ComponentInfo(
    "E2A41B7D-9F36-4A08-8B8D-1BA29E570F62",
    "显示剪切板内容",
    "\uE48C",
    "实时读取并显示显示剪切板内容"
)]
public partial class ClipboardContentComponent : ComponentBase<ClipboardContentSettings>, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private readonly System.Threading.SemaphoreSlim _refreshSemaphore = new(1, 1);
    private string _clipboardContent = "（等待剪切板文本内容更新…）";
    private string? _lastClipboardText;
    private bool _hasClipboardText;
    private bool _isLoaded;
    private long _loadGeneration;

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
        if (_isLoaded)
        {
            return;
        }

        _loadGeneration++;
        _isLoaded = true;
        _hasClipboardText = false;
        _timer.Start();
        _ = RefreshClipboardAsync();
    }

    private void ClipboardContentComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _loadGeneration++;
        _timer.Stop();
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        if (_isLoaded)
        {
            _ = RefreshClipboardAsync();
        }
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
        if (!_isLoaded || !_refreshSemaphore.Wait(0))
        {
            return;
        }

        var loadGeneration = _loadGeneration;
        try
        {
            if (!_isLoaded)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var clipboard = topLevel.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            var text = await ClipboardExtensions.TryGetTextAsync(clipboard);
            if (!_isLoaded || loadGeneration != _loadGeneration)
            {
                return;
            }

            if (_hasClipboardText && text == _lastClipboardText)
            {
                return;
            }

            _hasClipboardText = true;
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
            if (_isLoaded && loadGeneration == _loadGeneration)
            {
                _hasClipboardText = false;
                ClipboardContent = "（读取剪切板失败）";
            }
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }
}

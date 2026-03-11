using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using SystemTools.Models.ComponentSettings;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;

namespace SystemTools.Controls.Components;

[ComponentInfo(
    "5D2C0E65-8648-4A67-BBEA-3FA713B1CF8D",
    "本地一言",
    "\uE55D",
    "从本地 txt 文件轮播显示一言"
)]
public partial class LocalQuoteComponent : ComponentBase<LocalQuoteSettings>, INotifyPropertyChanged
{
    private readonly DispatcherTimer _carouselTimer;
    private readonly List<string> _quotes = [];
    private int _currentIndex = -1;
    private string _loadedPath = string.Empty;
    private string _currentQuote = "（请先在组件设置中选择 txt 文件）";
    private double _textOpacity = 1;

    public string CurrentQuote
    {
        get => _currentQuote;
        set
        {
            _currentQuote = value;
            OnPropertyChanged(nameof(CurrentQuote));
        }
    }

    public double TextOpacity
    {
        get => _textOpacity;
        set
        {
            _textOpacity = value;
            OnPropertyChanged(nameof(TextOpacity));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public LocalQuoteComponent()
    {
        InitializeComponent();
        _carouselTimer = new DispatcherTimer();
        _carouselTimer.Tick += OnCarouselTicked;
    }

    private void LocalQuoteComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        RefreshTimerInterval();
        LoadQuotesFromFile(Settings.QuotesFilePath, showFirstQuote: true);
        _carouselTimer.Start();
    }

    private void LocalQuoteComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _carouselTimer.Stop();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.CarouselIntervalSeconds))
        {
            RefreshTimerInterval();
            return;
        }

        if (e.PropertyName == nameof(Settings.QuotesFilePath))
        {
            LoadQuotesFromFile(Settings.QuotesFilePath, showFirstQuote: true);
        }
    }

    private void OnCarouselTicked(object? sender, EventArgs e)
    {
        if (_quotes.Count == 0)
        {
            return;
        }

        if (!string.Equals(_loadedPath, Settings.QuotesFilePath, StringComparison.Ordinal))
        {
            LoadQuotesFromFile(Settings.QuotesFilePath, showFirstQuote: true);
            return;
        }

        ShowNextQuote();
    }

    private void RefreshTimerInterval()
    {
        var interval = Math.Max(1, Settings.CarouselIntervalSeconds);
        _carouselTimer.Interval = TimeSpan.FromSeconds(interval);
    }

    private void LoadQuotesFromFile(string path, bool showFirstQuote)
    {
        _quotes.Clear();
        _currentIndex = -1;
        _loadedPath = path;

        if (string.IsNullOrWhiteSpace(path))
        {
            CurrentQuote = "（请先在组件设置中选择 txt 文件）";
            return;
        }

        if (!File.Exists(path))
        {
            CurrentQuote = "（txt 文件不存在）";
            return;
        }

        try
        {
            var lines = File.ReadAllLines(path)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (lines.Count == 0)
            {
                CurrentQuote = "（文件中没有可显示内容）";
                return;
            }

            _quotes.AddRange(lines);

            if (showFirstQuote)
            {
                ShowNextQuote();
            }
        }
        catch
        {
            CurrentQuote = "（读取 txt 文件失败）";
        }
    }

    private async void ShowNextQuote()
    {
        if (_quotes.Count == 0)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _quotes.Count;
        var next = _quotes[_currentIndex];

        if (!Settings.EnableAnimation)
        {
            TextOpacity = 1;
            CurrentQuote = next;
            return;
        }

        TextOpacity = 0;
        await System.Threading.Tasks.Task.Delay(120);
        CurrentQuote = next;
        TextOpacity = 1;
    }
}

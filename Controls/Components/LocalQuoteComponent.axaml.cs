using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private static readonly Thickness StableMargin = new(0);
    private static readonly Thickness EnterFromBottomMargin = new(0, 12, 0, -12);
    private static readonly Thickness ExitToTopMargin = new(0, -12, 0, 12);

    private readonly DispatcherTimer _carouselTimer;
    private readonly List<string> _quotes = [];
    private int _currentIndex = -1;
    private string _loadedPath = string.Empty;
    private bool _isAnimating;

    private string _currentQuote = "（请先在组件设置中选择 txt 文件）";
    private string _nextQuote = string.Empty;
    private double _currentTextOpacity = 1;
    private double _nextTextOpacity;
    private Thickness _currentTextMargin = StableMargin;
    private Thickness _nextTextMargin = EnterFromBottomMargin;

    public string CurrentQuote
    {
        get => _currentQuote;
        set
        {
            _currentQuote = value;
            OnPropertyChanged(nameof(CurrentQuote));
        }
    }

    public string NextQuote
    {
        get => _nextQuote;
        set
        {
            _nextQuote = value;
            OnPropertyChanged(nameof(NextQuote));
        }
    }

    public double CurrentTextOpacity
    {
        get => _currentTextOpacity;
        set
        {
            _currentTextOpacity = value;
            OnPropertyChanged(nameof(CurrentTextOpacity));
        }
    }

    public double NextTextOpacity
    {
        get => _nextTextOpacity;
        set
        {
            _nextTextOpacity = value;
            OnPropertyChanged(nameof(NextTextOpacity));
        }
    }

    public Thickness CurrentTextMargin
    {
        get => _currentTextMargin;
        set
        {
            _currentTextMargin = value;
            OnPropertyChanged(nameof(CurrentTextMargin));
        }
    }

    public Thickness NextTextMargin
    {
        get => _nextTextMargin;
        set
        {
            _nextTextMargin = value;
            OnPropertyChanged(nameof(NextTextMargin));
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
        if (_quotes.Count == 0 || _isAnimating)
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
        ResetAnimationVisualState();

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
            ResetAnimationVisualState();
            CurrentQuote = next;
            return;
        }

        _isAnimating = true;
        NextQuote = next;
        NextTextOpacity = 0;
        NextTextMargin = EnterFromBottomMargin;

        await Task.Delay(20);
        CurrentTextOpacity = 0;
        CurrentTextMargin = ExitToTopMargin;
        NextTextOpacity = 1;
        NextTextMargin = StableMargin;

        await Task.Delay(260);
        CurrentQuote = next;
        ResetAnimationVisualState();
        _isAnimating = false;
    }

    private void ResetAnimationVisualState()
    {
        CurrentTextOpacity = 1;
        CurrentTextMargin = StableMargin;
        NextTextOpacity = 0;
        NextTextMargin = EnterFromBottomMargin;
        NextQuote = string.Empty;
    }
}

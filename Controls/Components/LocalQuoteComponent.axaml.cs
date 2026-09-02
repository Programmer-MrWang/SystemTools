using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
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
    private const double SwapMotionOffset = 20;

    private readonly DispatcherTimer? _carouselTimer;
    private readonly ILessonsService? _lessonsService;
    private readonly List<string> _quotes = [];
    private readonly Animation? _swapOutAnimation;
    private readonly Animation? _swapInAnimation;
    private readonly Random _random = new();
    private int _currentIndex = -1;
    private string _loadedPath = string.Empty;
    private bool _isAnimating;
    private string _currentQuote = "（请先在组件设置中选择 txt 文件）";
    private DateTime _displayStartedAt = DateTime.UtcNow;
    private double _currentCycleDurationSeconds = 6;

    public string CurrentQuote
    {
        get => _currentQuote;
        set
        {
            _currentQuote = value;
            OnPropertyChanged(nameof(CurrentQuote));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public double CurrentProgressPercent { get; private set; }
    public bool ShowTopProgressBar => Settings.ShowProgressBar && Settings.ProgressBarPosition == LocalQuoteProgressBarPosition.Top;
    public bool ShowBottomProgressBar => Settings.ShowProgressBar && Settings.ProgressBarPosition == LocalQuoteProgressBarPosition.Bottom;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    static LocalQuoteComponent()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public LocalQuoteComponent()
    {
        InitializeComponent();
    }

    public LocalQuoteComponent(ILessonsService lessonsService)
    {
        _lessonsService = lessonsService;
        InitializeComponent();

        _carouselTimer = new DispatcherTimer();
        _carouselTimer.Tick += OnCarouselTicked;

        _swapOutAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            FillMode = FillMode.Forward,
            Easing = new QuadraticEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, 0d),
                        new Setter(Visual.OpacityProperty, 1d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, SwapMotionOffset),
                        new Setter(Visual.OpacityProperty, 0d)
                    }
                }
            }
        };

        _swapInAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            FillMode = FillMode.Forward,
            Easing = new QuadraticEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, -SwapMotionOffset),
                        new Setter(Visual.OpacityProperty, 0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, 0d),
                        new Setter(Visual.OpacityProperty, 1d)
                    }
                }
            }
        };
    }

    private void LocalQuoteComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        _lessonsService.PreMainTimerTicked += LessonsServiceOnPreMainTimerTicked;
        
        LoadQuotesFromFile(Settings.QuotesFilePath, showFirstQuote: false);
        
        RestoreStateAndStartTimer();
    }

    private void LocalQuoteComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _lessonsService.PreMainTimerTicked -= LessonsServiceOnPreMainTimerTicked;
        _carouselTimer.Stop();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.CarouselIntervalSeconds))
        {
            RefreshTimerInterval();
            EnsureTimersForQuoteState();
            return;
        }

        if (e.PropertyName is nameof(Settings.ShowProgressBar) or nameof(Settings.ProgressBarPosition))
        {
            OnPropertyChanged(nameof(ShowTopProgressBar));
            OnPropertyChanged(nameof(ShowBottomProgressBar));
            return;
        }

        if (e.PropertyName == nameof(Settings.QuotesFilePath))
        {
            LoadQuotesFromFile(Settings.QuotesFilePath, showFirstQuote: true);
            EnsureTimersForQuoteState();
        }

        if (e.PropertyName == nameof(Settings.PlaybackOrder))
        {
            ShowNextQuote();
            EnsureTimersForQuoteState();
        }
    }

    private void RestoreStateAndStartTimer()
    {
        if (_quotes.Count == 0)
        {
            return;
        }

        double initialDelay = Settings.CarouselIntervalSeconds;

        if (Settings.IsPersistenceEnabled)
        {
            if (Settings.LastIndex >= 0 && Settings.LastIndex < _quotes.Count)
            {
                _currentIndex = Settings.LastIndex;
            }
            else
            {
                _currentIndex = 0;
            }
            
            CurrentQuote = _quotes[_currentIndex];

            var elapsed = (DateTime.Now - Settings.LastSwitchTime).TotalSeconds;
            
            if (elapsed >= 0 && elapsed < Settings.CarouselIntervalSeconds)
            {
                initialDelay = Settings.CarouselIntervalSeconds - elapsed;
            }
            else
            {
                initialDelay = 0.5; 
            }
        }
        else
        {
            ShowNextQuote();
        }

        _carouselTimer.Interval = TimeSpan.FromSeconds(initialDelay);
        RestartProgressCycle(initialDelay);
        _carouselTimer.Start();
    }

    private void OnCarouselTicked(object? sender, EventArgs e)
    {
        if (_quotes.Count == 0 || _isAnimating)
        {
            return;
        }

        if (Math.Abs(_carouselTimer.Interval.TotalSeconds - Settings.CarouselIntervalSeconds) > 0.1)
        {
            RefreshTimerInterval();
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
        var interval = Math.Clamp(Settings.CarouselIntervalSeconds, 1, 8000);
        _carouselTimer.Interval = TimeSpan.FromSeconds(interval);
        RestartProgressCycle(interval);
    }

    private void LoadQuotesFromFile(string path, bool showFirstQuote)
    {
        _quotes.Clear();
        _currentIndex = -1;
        _loadedPath = path;
        ResetVisualState();

        if (string.IsNullOrWhiteSpace(path))
        {
            _carouselTimer.Stop();
            CurrentQuote = "（请先在组件设置中选择 txt 文件）";
            return;
        }

        if (!File.Exists(path))
        {
            _carouselTimer.Stop();
            CurrentQuote = "（txt 文件不存在）";
            return;
        }

        try
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        _quotes.Add(trimmed);
                    }
                }
            }

            if (_quotes.Count == 0)
            {
                _carouselTimer.Stop();
                CurrentQuote = "（文件中没有可显示内容）";
                return;
            }

            if (showFirstQuote)
            {
                ShowNextQuote();
            }
        }
        catch
        {
            _carouselTimer.Stop();
            CurrentQuote = "（读取 txt 文件失败）";
        }
    }

    private async void ShowNextQuote()
    {
        if (_quotes.Count == 0 || _isAnimating)
        {
            return;
        }
        
        RestartProgressCycle(_carouselTimer.Interval.TotalSeconds);

        _currentIndex = Settings.PlaybackOrder == LocalQuotePlaybackOrder.Random
            ? _random.Next(_quotes.Count)
            : (_currentIndex + 1) % _quotes.Count;
        var next = _quotes[_currentIndex];

        if (Settings.IsPersistenceEnabled)
        {
            Settings.LastIndex = _currentIndex;
            Settings.LastSwitchTime = DateTime.Now;
        }

        if (!Settings.EnableAnimation)
        {
            ResetVisualState();
            CurrentQuote = next;
            return;
        }

        _isAnimating = true;
        try
        {
            await _swapOutAnimation.RunAsync(QuoteTextBlock);
            CurrentQuote = next;
            await _swapInAnimation.RunAsync(QuoteTextBlock);
        }
        catch
        {
            CurrentQuote = next;
            ResetVisualState();
        }
        finally
        {
            _isAnimating = false;
        }
    }

    private void ResetVisualState()
    {
        QuoteTextBlock.Opacity = 1;

        if (QuoteTextBlock.RenderTransform is TranslateTransform transform)
        {
            transform.Y = 0;
        }
        else
        {
            QuoteTextBlock.RenderTransform = new TranslateTransform();
        }
    }

    private void RestartProgressCycle(double durationSeconds)
    {
        _currentCycleDurationSeconds = Math.Max(1, durationSeconds);
        _displayStartedAt = DateTime.UtcNow;
        UpdateProgressState();
    }

    private void UpdateProgressState()
    {
        if (_quotes.Count == 0 || !Settings.ShowProgressBar)
        {
            SetProgress(0);
            return;
        }

        var elapsed = (DateTime.UtcNow - _displayStartedAt).TotalSeconds;
        var ratio = Math.Clamp(elapsed / _currentCycleDurationSeconds, 0, 1);
        SetProgress(ratio * 100);
    }

    private void SetProgress(double progress)
    {
        if (Math.Abs(CurrentProgressPercent - progress) < 0.1)
        {
            return;
        }

        CurrentProgressPercent = progress;
        OnPropertyChanged(nameof(CurrentProgressPercent));
    }

    private void EnsureTimersForQuoteState()
    {
        if (_quotes.Count == 0)
        {
            _carouselTimer.Stop();
            return;
        }

        if (!_carouselTimer.IsEnabled)
        {
            var interval = Math.Clamp(Settings.CarouselIntervalSeconds, 1, 8000);
            _carouselTimer.Interval = TimeSpan.FromSeconds(interval);
            RestartProgressCycle(interval);
            _carouselTimer.Start();
        }

    }

    private void LessonsServiceOnPreMainTimerTicked(object? sender, EventArgs e)
    {
        UpdateProgressState();
    }
}

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
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
    private const double SwapMotionOffset = 20;

    private readonly DispatcherTimer _carouselTimer;
    private readonly List<string> _quotes = [];
    private readonly Animation _swapOutAnimation;
    private readonly Animation _swapInAnimation;
    private int _currentIndex = -1;
    private string _loadedPath = string.Empty;
    private bool _isAnimating;
    private string _currentQuote = "（请先在组件设置中选择 txt 文件）";

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

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public LocalQuoteComponent()
    {
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
        ResetVisualState();

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
        if (_quotes.Count == 0 || _isAnimating)
        {
            return;
        }

        _currentIndex = (_currentIndex + 1) % _quotes.Count;
        var next = _quotes[_currentIndex];

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
}

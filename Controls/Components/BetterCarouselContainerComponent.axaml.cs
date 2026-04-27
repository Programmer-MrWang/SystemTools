using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Components;
using SystemTools.Models.ComponentSettings;

namespace SystemTools.Controls.Components;

[ContainerComponent]
[ComponentInfo("A7C3455E-6A4E-4D4D-9D0D-7C6FCB5E1E3A", "更好的轮播容器", "\uF0DB", "带有可单独设置组件显示时长等高级功能的轮播容器")]
public partial class BetterCarouselContainerComponent : ComponentBase<BetterCarouselContainerSettings>, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly IRulesetService _rulesetService;
    private readonly Random _random = new();
    
    private Animation? _slideInAnimation;
    private Animation? _slideOutAnimation;
    private Animation? _flashInAnimation;
    private Animation? _flashOutAnimation;
    private Animation? _fadeInAnimation;
    private Animation? _fadeOutAnimation;

    private int _selectedIndex;
    private int _playDirection = 1;
    private DateTime _displayStartedAt = DateTime.UtcNow;
    private bool _isLoaded;
    private bool _isAnimating;

    public static readonly AttachedProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.RegisterAttached<BetterCarouselContainerComponent, Control, bool>("IsAnimationEnabled", inherits: true);

    public static void SetIsAnimationEnabled(Control obj, bool value) => obj.SetValue(IsAnimationEnabledProperty, value);
    public static bool GetIsAnimationEnabled(Control obj) => obj.GetValue(IsAnimationEnabledProperty);

    public static readonly AttachedProperty<int> AnimationStyleProperty =
        AvaloniaProperty.RegisterAttached<BetterCarouselContainerComponent, Control, int>("AnimationStyle", inherits: true);

    public static void SetAnimationStyle(Control obj, int value) => obj.SetValue(AnimationStyleProperty, value);
    public static int GetAnimationStyle(Control obj) => obj.GetValue(AnimationStyleProperty);

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex || _isAnimating)
            {
                return;
            }

            var oldIndex = _selectedIndex;
            _selectedIndex = value;
            OnPropertyChanged(nameof(SelectedIndex));
            
            if (Settings.IsAnimationEnabled && oldIndex >= 0 && oldIndex < Settings.Children.Count)
            {
                _ = AnimateTransitionAsync(oldIndex, value);
            }
            
            RestartProgress();
        }
    }

    public double CurrentProgressPercent { get; private set; }

    public int AnimationStyleValue => (int)Settings.AnimationStyle;
    public bool ShowTopProgressBar => Settings.ShowProgressBar && Settings.ProgressBarPosition == BetterCarouselProgressBarPosition.Top;
    public bool ShowBottomProgressBar => Settings.ShowProgressBar && Settings.ProgressBarPosition == BetterCarouselProgressBarPosition.Bottom;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public BetterCarouselContainerComponent(IRulesetService rulesetService)
    {
        _rulesetService = rulesetService;
        InitializeComponent();
        InitializeAnimations();
        _timer.Tick += OnTimerTick;
    }

    private void InitializeAnimations()
    {
        _slideInAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            FillMode = FillMode.Forward,
            Easing = new QuadraticEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, -30d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                }
            }
        };

        _slideOutAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            FillMode = FillMode.Forward,
            Easing = new QuadraticEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, 30d)
                    }
                }
            }
        };

        _flashInAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.75),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.6d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d)
                    }
                }
            }
        };

        _flashOutAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d)
                    }
                }
            }
        };

        _fadeInAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            FillMode = FillMode.Forward,
            Easing = new QuadraticEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(Visual.OpacityProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, 1d) }
                }
            }
        };

        _fadeOutAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            FillMode = FillMode.Forward,
            Easing = new QuadraticEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(Visual.OpacityProperty, 1d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, 0d) }
                }
            }
        };
    }

    private async Task AnimateTransitionAsync(int fromIndex, int toIndex)
{
    if (_isAnimating || CarouselListBox == null) return;
    
    _isAnimating = true;
    
    try
    {
        var container = CarouselListBox.ContainerFromIndex(fromIndex);
        if (container is ListBoxItem fromItem)
        {
            if (fromItem.RenderTransform == null)
            {
                fromItem.RenderTransform = new TranslateTransform();
            }

            var outAnimation = Settings.AnimationStyle switch
            {
                BetterCarouselAnimationStyle.Flash => _flashOutAnimation,
                BetterCarouselAnimationStyle.Fade => _fadeOutAnimation,
                _ => _slideOutAnimation
            };
            
            if (outAnimation != null)
            {
                await outAnimation.RunAsync(fromItem);
            }
            
            fromItem.Opacity = 0;
            fromItem.IsVisible = false;
            fromItem.RenderTransform = null;
        }

        var toContainer = CarouselListBox.ContainerFromIndex(toIndex);
        if (toContainer is ListBoxItem toItem)
        {
            if (toItem.RenderTransform == null)
            {
                toItem.RenderTransform = new TranslateTransform();
            }

            toItem.Opacity = 0;
            toItem.IsVisible = true;

            var inAnimation = Settings.AnimationStyle switch
            {
                BetterCarouselAnimationStyle.Flash => _flashInAnimation,
                BetterCarouselAnimationStyle.Fade => _fadeInAnimation,
                _ => _slideInAnimation
            };
            
            if (inAnimation != null)
            {
                await inAnimation.RunAsync(toItem);
            }
            
            toItem.Opacity = 1;
            toItem.RenderTransform = null;
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Animation error: {ex.Message}");
    }
    finally
    {
        _isAnimating = false;
    }
}

    private void BetterCarouselContainerComponent_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        Settings.Children.CollectionChanged += OnChildrenCollectionChanged;
        Settings.ComponentDisplayDurations.CollectionChanged += OnDurationCollectionChanged;
        _rulesetService.StatusUpdated += OnRulesetStatusUpdated;
        SubscribeChildren(Settings.Children);
        Settings.NormalizeDisplayDurations();
        EnsureSelectedIndexValid();
        UpdateProgressState();
        _timer.Start();
    }

    private void BetterCarouselContainerComponent_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        _timer.Stop();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Settings.Children.CollectionChanged -= OnChildrenCollectionChanged;
        Settings.ComponentDisplayDurations.CollectionChanged -= OnDurationCollectionChanged;
        _rulesetService.StatusUpdated -= OnRulesetStatusUpdated;
        UnsubscribeChildren(Settings.Children);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateProgressState();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Settings.AnimationStyle))
        {
            OnPropertyChanged(nameof(AnimationStyleValue));
        }

        if (e.PropertyName is nameof(Settings.ShowProgressBar) or nameof(Settings.ProgressBarPosition))
        {
            OnPropertyChanged(nameof(ShowTopProgressBar));
            OnPropertyChanged(nameof(ShowBottomProgressBar));
        }

        if (e.PropertyName is nameof(Settings.RotationMode) or nameof(Settings.IsAnimationEnabled) or nameof(Settings.ShowProgressBar))
        {
            UpdateProgressState(resetWhenIdle: true);
        }

        if (e.PropertyName == nameof(Settings.ComponentDisplayDurations))
        {
            Settings.NormalizeDisplayDurations();
            RestartProgress();
        }

    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<ComponentSettings>())
            {
                item.PropertyChanged -= OnChildPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<ComponentSettings>())
            {
                item.PropertyChanged += OnChildPropertyChanged;
            }
        }

        Settings.NormalizeDisplayDurations();
        EnsureSelectedIndexValid();
        UpdateProgressState(resetWhenIdle: true);
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComponentSettings.HideOnRule) or nameof(ComponentSettings.HidingRules) or nameof(ComponentSettings.Id) or nameof(ComponentSettings.NameCache))
        {
            UpdateProgressState(resetWhenIdle: true);
        }
    }

    private void OnRulesetStatusUpdated(object? sender, EventArgs e)
    {
        UpdateProgressState(resetWhenIdle: true);
    }

    private void OnDurationCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Settings.NormalizeDisplayDurations();
        RestartProgress();
        UpdateProgressState();
    }

    private void SubscribeChildren(System.Collections.Generic.IEnumerable<ComponentSettings> children)
    {
        foreach (var child in children)
        {
            child.PropertyChanged += OnChildPropertyChanged;
        }
    }

    private void UnsubscribeChildren(System.Collections.Generic.IEnumerable<ComponentSettings> children)
    {
        foreach (var child in children)
        {
            child.PropertyChanged -= OnChildPropertyChanged;
        }
    }

    private void EnsureSelectedIndexValid()
    {
        if (Settings.Children.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex < 0 || SelectedIndex >= Settings.Children.Count || !IsChildDisplayable(SelectedIndex))
        {
            SelectedIndex = FindFirstDisplayableIndex();
        }
    }

    private void UpdateProgressState(bool resetWhenIdle = false)
    {
        var displayable = GetDisplayableIndexes();
        if (displayable.Length == 0)
        {
            SelectedIndex = -1;
            SetCurrentProgressPercent(0);
            if (resetWhenIdle)
            {
                RestartProgress();
            }
            return;
        }

        if (!displayable.Contains(SelectedIndex))
        {
            SelectedIndex = displayable[0];
        }

        if (displayable.Length <= 1)
        {
            if (resetWhenIdle)
            {
                _displayStartedAt = DateTime.UtcNow;
            }
            SetCurrentProgressPercent(100);
            return;
        }

        var duration = TimeSpan.FromSeconds(Settings.GetDisplayDurationSeconds(SelectedIndex));
        var elapsed = DateTime.UtcNow - _displayStartedAt;
        if (elapsed >= duration)
        {
            AdvanceToNext(displayable);
            elapsed = DateTime.UtcNow - _displayStartedAt;
            duration = TimeSpan.FromSeconds(Settings.GetDisplayDurationSeconds(SelectedIndex));
        }

        var percent = duration.TotalMilliseconds <= 0
            ? 100
            : Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100);
        SetCurrentProgressPercent(percent);
    }

    private void AdvanceToNext(int[] displayableIndexes)
    {
        if (displayableIndexes.Length == 0)
        {
            return;
        }

        var currentPos = Array.IndexOf(displayableIndexes, SelectedIndex);
        if (currentPos < 0)
        {
            SelectedIndex = displayableIndexes[0];
            RestartProgress();
            return;
        }

        var nextIndex = SelectedIndex;
        switch (Settings.RotationMode)
        {
            case BetterCarouselRotationMode.Random:
                if (displayableIndexes.Length > 1)
                {
                    do
                    {
                        nextIndex = displayableIndexes[_random.Next(displayableIndexes.Length)];
                    } while (nextIndex == SelectedIndex);
                }
                break;
            case BetterCarouselRotationMode.PingPong:
                if (currentPos + _playDirection >= displayableIndexes.Length || currentPos + _playDirection < 0)
                {
                    _playDirection *= -1;
                }
                nextIndex = displayableIndexes[Math.Clamp(currentPos + _playDirection, 0, displayableIndexes.Length - 1)];
                break;
            case BetterCarouselRotationMode.Loop:
            default:
                nextIndex = displayableIndexes[(currentPos + 1) % displayableIndexes.Length];
                break;
        }

        SelectedIndex = nextIndex;
        RestartProgress();
    }

    private int FindFirstDisplayableIndex()
    {
        var indexes = GetDisplayableIndexes();
        return indexes.Length == 0 ? -1 : indexes[0];
    }

    private int[] GetDisplayableIndexes()
    {
        return Settings.Children
            .Select((child, index) => new { child, index })
            .Where(x => !x.child.HideOnRule || !_rulesetService.IsRulesetSatisfied(x.child.HidingRules))
            .Select(x => x.index)
            .ToArray();
    }

    private bool IsChildDisplayable(int index)
    {
        if (index < 0 || index >= Settings.Children.Count)
        {
            return false;
        }

        var child = Settings.Children[index];
        return !child.HideOnRule || !_rulesetService.IsRulesetSatisfied(child.HidingRules);
    }

    private void RestartProgress()
    {
        _displayStartedAt = DateTime.UtcNow;
        SetCurrentProgressPercent(0);
    }

    private void SetCurrentProgressPercent(double value)
    {
        if (Math.Abs(CurrentProgressPercent - value) < 0.01)
        {
            return;
        }

        CurrentProgressPercent = value;
        OnPropertyChanged(nameof(CurrentProgressPercent));
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using ClassIsland.Shared;
using SystemTools.Models.ComponentSettings;

namespace SystemTools.Controls.Components;

[ContainerComponent]
[ComponentInfo("E0F5A2B4-4A8D-4B62-A061-2ACF5920B2B3", "更好的轮播容器", "\uE8EE", "支持单组件独立时长、进度条和闪烁动画的轮播容器。")]
public partial class BetterSlideComponent : ComponentBase<BetterSlideComponentSettings>, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public static readonly AttachedProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.RegisterAttached<BetterSlideComponent, Control, bool>("IsAnimationEnabled", inherits: true);

    public static readonly AttachedProperty<int> AnimationModeProperty =
        AvaloniaProperty.RegisterAttached<BetterSlideComponent, Control, int>("AnimationMode", inherits: true);

    public static void SetIsAnimationEnabled(Control obj, bool value) => obj.SetValue(IsAnimationEnabledProperty, value);

    public static bool GetIsAnimationEnabled(Control obj) => obj.GetValue(IsAnimationEnabledProperty);

    public static void SetAnimationMode(Control obj, int value) => obj.SetValue(AnimationModeProperty, value);

    public static int GetAnimationMode(Control obj) => obj.GetValue(AnimationModeProperty);

    private readonly DispatcherTimer _switchTimer = new();

    private readonly DispatcherTimer _progressTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    private readonly Random _random = new();

    private readonly Queue<int> _randomPlaylist = [];

    private readonly IRulesetService _rulesetService = IAppHost.GetService<IRulesetService>();

    private DateTime _showingStartAt = DateTime.Now;

    private double _currentDurationSeconds = 5;

    private int _selectedIndex;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            MainListBox.SelectedIndex = value;
        }
    }

    private int _playingDirection = 1;

    private double _progressPercent;

    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            _progressPercent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressPercent)));
        }
    }

    public BetterSlideComponent()
    {
        InitializeComponent();
    }

    private void BetterSlideComponent_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        GridRoot.DataContext = this;
        Settings.EnsureDurationEntries();

        _switchTimer.Tick += SwitchTimerOnTick;
        _progressTimer.Tick += ProgressTimerOnTick;
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        Settings.Children.CollectionChanged += ChildrenOnCollectionChanged;

        foreach (var item in Settings.ComponentDurations)
        {
            item.PropertyChanged += DurationItemOnPropertyChanged;
        }

        Settings.ComponentDurations.CollectionChanged += ComponentDurationsOnCollectionChanged;

        SelectedIndex = 0;
        StartCurrentComponentCycle();
        _switchTimer.Start();
        _progressTimer.Start();
    }

    private void BetterSlideComponent_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _switchTimer.Stop();
        _progressTimer.Stop();

        _switchTimer.Tick -= SwitchTimerOnTick;
        _progressTimer.Tick -= ProgressTimerOnTick;
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        Settings.Children.CollectionChanged -= ChildrenOnCollectionChanged;
        Settings.ComponentDurations.CollectionChanged -= ComponentDurationsOnCollectionChanged;

        foreach (var item in Settings.ComponentDurations)
        {
            item.PropertyChanged -= DurationItemOnPropertyChanged;
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BetterSlideComponentSettings.SlideMode))
        {
            _randomPlaylist.Clear();
            _playingDirection = 1;
        }
    }

    private void ChildrenOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Settings.EnsureDurationEntries();

        if (SelectedIndex >= Settings.Children.Count)
        {
            SelectedIndex = 0;
        }

        _randomPlaylist.Clear();
        StartCurrentComponentCycle();
    }

    private void ComponentDurationsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is ComponentDurationSetting durationItem)
                {
                    durationItem.PropertyChanged += DurationItemOnPropertyChanged;
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ComponentDurationSetting durationItem)
                {
                    durationItem.PropertyChanged -= DurationItemOnPropertyChanged;
                }
            }
        }
    }

    private void DurationItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComponentDurationSetting.DurationSeconds))
        {
            StartCurrentComponentCycle();
        }
    }

    private void SwitchTimerOnTick(object? sender, EventArgs e)
    {
        if (Settings.Children.Count <= 1)
        {
            SelectedIndex = 0;
            StartCurrentComponentCycle();
            return;
        }

        var checkedIndices = new HashSet<int>();

        do
        {
            ShowNext();

            if (!checkedIndices.Contains(SelectedIndex)
                && Settings.Children[SelectedIndex].HideOnRule
                && _rulesetService.IsRulesetSatisfied(Settings.Children[SelectedIndex].HidingRules))
            {
                checkedIndices.Add(SelectedIndex);
            }

            if (checkedIndices.Count >= Settings.Children.Count)
            {
                break;
            }
        } while (checkedIndices.Contains(SelectedIndex));

        StartCurrentComponentCycle();
    }

    private void ShowNext()
    {
        switch (Settings.SlideMode)
        {
            case 0:
                SelectedIndex = SelectedIndex + 1 >= Settings.Children.Count ? 0 : SelectedIndex + 1;
                break;
            case 1:
                if (_randomPlaylist.Count == 0)
                {
                    CreateRandomPlaylist();
                }

                SelectedIndex = _randomPlaylist.Dequeue();
                break;
            case 2:
                var target = SelectedIndex + _playingDirection;
                if (target < 0 || target >= Settings.Children.Count)
                {
                    _playingDirection = -_playingDirection;
                }

                SelectedIndex += _playingDirection;
                break;
            default:
                SelectedIndex = SelectedIndex + 1 >= Settings.Children.Count ? 0 : SelectedIndex + 1;
                break;
        }
    }

    private void CreateRandomPlaylist()
    {
        _randomPlaylist.Clear();

        var list = new List<int>();
        for (var i = 0; i < Settings.Children.Count; i++)
        {
            list.Add(i);
        }

        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        foreach (var index in list)
        {
            _randomPlaylist.Enqueue(index);
        }
    }

    private void StartCurrentComponentCycle()
    {
        Settings.EnsureDurationEntries();

        _currentDurationSeconds = Settings.GetDurationSecondsFor(SelectedIndex);
        if (_currentDurationSeconds <= 0)
        {
            _currentDurationSeconds = 5;
        }

        _switchTimer.Interval = TimeSpan.FromSeconds(_currentDurationSeconds);
        _showingStartAt = DateTime.Now;
        ProgressPercent = 0;
    }

    private void ProgressTimerOnTick(object? sender, EventArgs e)
    {
        if (_currentDurationSeconds <= 0)
        {
            ProgressPercent = 0;
            return;
        }

        var elapsed = DateTime.Now - _showingStartAt;
        var percent = elapsed.TotalSeconds / _currentDurationSeconds * 100;
        ProgressPercent = Math.Clamp(percent, 0, 100);
    }
}

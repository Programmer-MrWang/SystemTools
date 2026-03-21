using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ClassIsland.Core.Abstractions.Models;
using CoreComponentSettings = ClassIsland.Core.Models.Components.ComponentSettings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

public enum BetterCarouselRotationMode
{
    [Description("循环")]
    Loop = 0,

    [Description("随机")]
    Random = 1,

    [Description("往复")]
    PingPong = 2
}

public enum BetterCarouselAnimationStyle
{
    [Description("翻页")]
    Slide = 0,

    [Description("闪烁")]
    Flash = 1
}

public partial class BetterCarouselContainerSettings : ObservableObject, IComponentContainerSettings
{
    public const double DefaultDisplayDurationSeconds = 15;

    private ObservableCollection<CoreComponentSettings> _children = new();

    [ObservableProperty]
    private ObservableCollection<double> _componentDisplayDurations = new();

    [ObservableProperty]
    private BetterCarouselRotationMode _rotationMode = BetterCarouselRotationMode.Loop;

    [ObservableProperty]
    private bool _isAnimationEnabled = true;

    [ObservableProperty]
    private BetterCarouselAnimationStyle _animationStyle = BetterCarouselAnimationStyle.Slide;

    [ObservableProperty]
    private bool _showProgressBar = true;

    public ObservableCollection<CoreComponentSettings> Children
    {
        get => _children;
        set
        {
            if (ReferenceEquals(value, _children))
            {
                return;
            }

            _children.CollectionChanged -= OnChildrenCollectionChanged;
            _children = value ?? new ObservableCollection<CoreComponentSettings>();
            _children.CollectionChanged += OnChildrenCollectionChanged;
            NormalizeDisplayDurations();
            OnPropertyChanged();
        }
    }

    public BetterCarouselContainerSettings()
    {
        _children.CollectionChanged += OnChildrenCollectionChanged;
        NormalizeDisplayDurations();
    }

    public double GetDisplayDurationSeconds(int index)
    {
        NormalizeDisplayDurations();
        if (index < 0 || index >= ComponentDisplayDurations.Count)
        {
            return DefaultDisplayDurationSeconds;
        }

        return SanitizeDuration(ComponentDisplayDurations[index]);
    }

    public void SetDisplayDurationSeconds(int index, double value)
    {
        NormalizeDisplayDurations();
        if (index < 0 || index >= ComponentDisplayDurations.Count)
        {
            return;
        }

        ComponentDisplayDurations[index] = SanitizeDuration(value);
    }

    public void NormalizeDisplayDurations()
    {
        while (ComponentDisplayDurations.Count < Children.Count)
        {
            ComponentDisplayDurations.Add(DefaultDisplayDurationSeconds);
        }

        while (ComponentDisplayDurations.Count > Children.Count)
        {
            ComponentDisplayDurations.RemoveAt(ComponentDisplayDurations.Count - 1);
        }

        for (var i = 0; i < ComponentDisplayDurations.Count; i++)
        {
            var sanitized = SanitizeDuration(ComponentDisplayDurations[i]);
            if (Math.Abs(ComponentDisplayDurations[i] - sanitized) > 0.0001)
            {
                ComponentDisplayDurations[i] = sanitized;
            }
        }
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewStartingIndex < 0 || e.NewItems == null)
                {
                    NormalizeDisplayDurations();
                    break;
                }

                for (var i = 0; i < e.NewItems.Count; i++)
                {
                    ComponentDisplayDurations.Insert(Math.Min(e.NewStartingIndex + i, ComponentDisplayDurations.Count), DefaultDisplayDurationSeconds);
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldStartingIndex < 0 || e.OldItems == null)
                {
                    NormalizeDisplayDurations();
                    break;
                }

                for (var i = 0; i < e.OldItems.Count && e.OldStartingIndex < ComponentDisplayDurations.Count; i++)
                {
                    ComponentDisplayDurations.RemoveAt(e.OldStartingIndex);
                }
                break;
            case NotifyCollectionChangedAction.Move:
                if (e.OldItems == null || e.OldItems.Count == 0 || e.OldStartingIndex < 0 || e.NewStartingIndex < 0)
                {
                    NormalizeDisplayDurations();
                    break;
                }

                if (e.OldItems.Count == 1)
                {
                    ComponentDisplayDurations.Move(e.OldStartingIndex, e.NewStartingIndex);
                }
                else
                {
                    NormalizeDisplayDurations();
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                NormalizeDisplayDurations();
                break;
            case NotifyCollectionChangedAction.Reset:
                NormalizeDisplayDurations();
                break;
        }
    }

    private static double SanitizeDuration(double seconds) => Math.Clamp(seconds, 1, 3600);
}

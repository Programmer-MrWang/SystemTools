using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Models.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemTools.Models.ComponentSettings;

namespace SystemTools.Controls.Components;

public partial class BetterCarouselDurationItem : ObservableObject
{
    private readonly BetterCarouselContainerSettings _settings;
    private readonly ComponentSettings _component;

    public int Index { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(_component.AssociatedComponentInfo.Name)
        ? (string.IsNullOrWhiteSpace(_component.NameCache) ? "未命名组件" : _component.NameCache)
        : _component.AssociatedComponentInfo.Name;

    public string Subtitle => $"第 {Index + 1} 个组件";

    public double DurationSeconds
    {
        get => _settings.GetDisplayDurationSeconds(Index);
        set
        {
            _settings.SetDisplayDurationSeconds(Index, value);
            OnPropertyChanged();
        }
    }

    public BetterCarouselDurationItem(int index, ComponentSettings component, BetterCarouselContainerSettings settings)
    {
        Index = index;
        _component = component;
        _settings = settings;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(DurationSeconds));
    }
}

public partial class BetterCarouselContainerSettingsControl : ComponentBase<BetterCarouselContainerSettings>, INotifyPropertyChanged
{
    public ObservableCollection<BetterCarouselDurationItem> DurationItems { get; } = new();

    public new event PropertyChangedEventHandler? PropertyChanged;

    public Array RotationModes { get; } = Enum.GetValues(typeof(BetterCarouselRotationMode));
    public Array AnimationStyles { get; } = Enum.GetValues(typeof(BetterCarouselAnimationStyle));

    public bool HasDurationItems => DurationItems.Count > 0;

    public BetterCarouselContainerSettingsControl()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Settings.Children.CollectionChanged += OnChildrenCollectionChanged;
        foreach (var child in Settings.Children)
        {
            child.PropertyChanged += OnChildPropertyChanged;
        }

        Settings.ComponentDisplayDurations.CollectionChanged += OnDurationCollectionChanged;
        RefreshDurationItems();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Settings.Children.CollectionChanged -= OnChildrenCollectionChanged;
        foreach (var child in Settings.Children)
        {
            child.PropertyChanged -= OnChildPropertyChanged;
        }

        Settings.ComponentDisplayDurations.CollectionChanged -= OnDurationCollectionChanged;
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

        RefreshDurationItems();
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComponentSettings.Id) or nameof(ComponentSettings.NameCache))
        {
            RefreshDurationItems();
        }
    }

    private void OnDurationCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshDurationItems();
    }

    private void RefreshDurationItems()
    {
        Settings.NormalizeDisplayDurations();
        DurationItems.Clear();

        for (var i = 0; i < Settings.Children.Count; i++)
        {
            DurationItems.Add(new BetterCarouselDurationItem(i, Settings.Children[i], Settings));
        }

        OnPropertyChanged(nameof(HasDurationItems));
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

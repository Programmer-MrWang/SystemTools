using ClassIsland.Core.Abstractions.Controls;
using Avalonia.VisualTree;
using System.Collections.Specialized;
using Avalonia;
using SystemTools.Models.ComponentSettings;

namespace SystemTools.Controls.Components;

public partial class BetterSlideSettingsControl : ComponentBase<BetterSlideComponentSettings>
{
    public BetterSlideSettingsControl()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Settings.EnsureDurationEntries();
        Settings.Children.CollectionChanged += ChildrenOnCollectionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Settings.Children.CollectionChanged -= ChildrenOnCollectionChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void ChildrenOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Settings.EnsureDurationEntries();
    }
}

// Adapted from ClassIsland.Android/Helpers/NavigationRepeaterAutomationFix.cs
// at host commit 3afce2714b6ec50d4bd2a3baac95a71d96ce3b9b (GPL-3.0).
// Host-side workaround only; never compiled into or installed by the plugin.
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.Desktop.Helpers;

internal static class NavigationRepeaterAutomationFix
{
    static NavigationRepeaterAutomationFix()
    {
        StyledElement.TemplatedParentProperty.Changed.AddClassHandler<FAItemsRepeater>((repeater, _) =>
        {
            if (repeater.TemplatedParent is FANavigationView or FANavigationViewItem)
                GetAutomationPeer(repeater) ??= new NavigationRepeaterAutomationPeer(repeater);
        });
    }

    internal static void Initialize() { }

    // Same field contract and scoped peer replacement already used by the host's
    // Android workaround. Recheck it when upgrading Avalonia/FluentAvalonia.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_automationPeer")]
    private static extern ref AutomationPeer? GetAutomationPeer(Control control);

    private sealed class NavigationRepeaterAutomationPeer(FAItemsRepeater owner)
        : FAItemsRepeaterAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
            Owner.GetVisualChildren().Any() ? base.GetChildrenCore() : [];
    }
}

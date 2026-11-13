using System.Reflection;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using ClassIsland.Desktop.Helpers;
using FluentAvalonia.UI.Controls;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine($"PASS {++checks}: {message}");
}

// Regression reproduces the dependency fault without a native AX callback,
// so the exception is catchable instead of aborting the desktop host.
var empty = new ProbeRepeater();
try
{
    new FAItemsRepeaterAutomationPeer(empty).GetChildren();
    throw new Exception("Expected FluentAvalonia 3.0.0 regression did not reproduce; re-evaluate workaround.");
}
catch (NullReferenceException)
{
    Check(true, "Unpatched empty repeater reproduces NullReferenceException");
}

var peerType = typeof(NavigationRepeaterAutomationFix).GetNestedType("NavigationRepeaterAutomationPeer", BindingFlags.NonPublic)!;
AutomationPeer Guard(FAItemsRepeater owner) => (AutomationPeer)Activator.CreateInstance(peerType, owner)!;
Check(Guard(empty).GetChildren()!.Count == 0, "Guarded empty repeater returns an empty accessibility list");

var populated = new ProbeRepeater();
populated.Add(new Button { Content = "Retained item" });
var expected = new FAItemsRepeaterAutomationPeer(populated).GetChildren();
var actual = Guard(populated).GetChildren();
Check(actual!.Count == expected!.Count, "Nonempty repeater keeps original enumeration behavior");

NavigationRepeaterAutomationFix.Initialize();
var navigation = new ProbeRepeater();
typeof(StyledElement).GetProperty("TemplatedParent")!.SetValue(navigation, new FANavigationView());
var installed = ControlAutomationPeer.CreatePeerForElement(navigation)!;
Check(installed.GetType() == peerType, "Class handler installs the guard on navigation repeaters");
Check(installed.GetChildren()!.Count == 0, "Installed navigation peer enumerates safely");

var unrelated = new ProbeRepeater();
typeof(StyledElement).GetProperty("TemplatedParent")!.SetValue(unrelated, new Button());
Check(ControlAutomationPeer.CreatePeerForElement(unrelated)!.GetType() != peerType, "Unrelated repeater peer is left intact");
Console.WriteLine($"PASS: {checks} host accessibility regression checks");

sealed class ProbeRepeater : FAItemsRepeater
{
    public void Add(Control control) => VisualChildren.Add(control);
}

using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
namespace SystemTools;

[ActionInfo("SystemTools.M1.RecordDiagnostics", "记录平台诊断（M1）", "\uE6F3", true, "SystemTools M1 诊断")]
public sealed class RecordDiagnosticsAction(DiagnosticService diagnostics) : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        InterruptCancellationToken.ThrowIfCancellationRequested();
        diagnostics.Capture("host-action");
    }
}

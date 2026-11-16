using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
namespace SystemTools;

[ActionInfo("SystemTools.M1.RecordDiagnostics", "导出平台诊断", "\uE6F3", true, "SystemTools 内部诊断")]
public sealed class RecordDiagnosticsAction(DiagnosticService diagnostics) : ActionBase
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        InterruptCancellationToken.ThrowIfCancellationRequested();
        diagnostics.Capture("host-action");
    }
}

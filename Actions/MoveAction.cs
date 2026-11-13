using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Common.Files;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.Move", "移动", "\uE6E7", false)]
public class MoveAction(ILogger<MoveAction> logger, FileOperations files) : ActionBase<MoveSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        InterruptCancellationToken.ThrowIfCancellationRequested();
        if (Settings is null || string.IsNullOrWhiteSpace(Settings.SourcePath) || string.IsNullOrWhiteSpace(Settings.DestinationPath)) { logger.LogWarning("路径为空"); return; }
        await files.TransferAsync(Settings.SourcePath, Settings.DestinationPath, Settings.OperationType != "文件", true, InterruptCancellationToken);
        logger.LogInformation("移动完成");
    }
}

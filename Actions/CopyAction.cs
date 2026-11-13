using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Common.Files;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.Copy", "复制", "\uE6AB", false)]
public class CopyAction(ILogger<CopyAction> logger, FileOperations files) : ActionBase<CopySettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        InterruptCancellationToken.ThrowIfCancellationRequested();
        if (Settings is null || string.IsNullOrWhiteSpace(Settings.SourcePath) || string.IsNullOrWhiteSpace(Settings.DestinationPath)) { logger.LogWarning("路径为空"); return; }
        await files.TransferAsync(Settings.SourcePath, Settings.DestinationPath, Settings.OperationType != "文件", false, InterruptCancellationToken);
        logger.LogInformation("复制完成");
    }
}

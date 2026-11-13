using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Common.Files;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.Delete", "删除", "\uE61D", false)]
public class DeleteAction(ILogger<DeleteAction> logger, FileOperations files) : ActionBase<DeleteSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        InterruptCancellationToken.ThrowIfCancellationRequested();
        if (Settings is null || string.IsNullOrWhiteSpace(Settings.TargetPath)) { logger.LogWarning("路径为空"); return; }
        await files.DeleteAsync(Settings.TargetPath, Settings.OperationType != "文件", InterruptCancellationToken);
        logger.LogInformation("删除完成");
    }
}

using System;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Platforms.Abstraction;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ShowToast", "拉起自定义Windows通知", "\uE3E4", false)]
public class ShowToastAction : ActionBase<ShowToastSettings>
{
    private readonly ILogger<ShowToastAction> _logger;

    public ShowToastAction(ILogger<ShowToastAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ShowToastAction OnInvoke 开始");

        var title = Settings.Title;
        var content = Settings.Content;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("通知标题和内容均为空，跳过显示");
            return;
        }

        try
        {
            await PlatformServices.DesktopToastService.ShowToastAsync(
                title ?? "SystemTools",
                content ?? string.Empty,
                () =>
                {
                    _logger.LogInformation("用户点击了通知");
                }
            );

            _logger.LogInformation("已显示通知: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示通知失败");
        }

        await base.OnInvoke();
        _logger.LogDebug("ShowToastAction OnInvoke 完成");
    }
}
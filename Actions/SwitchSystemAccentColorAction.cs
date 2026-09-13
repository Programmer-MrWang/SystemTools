using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using SystemTools.Helpers;
using SystemTools.Services;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SwitchSystemAccentColor", "切换系统强调色", "\uE523", false)]
public class SwitchSystemAccentColorAction(ILogger<SwitchSystemAccentColorAction> logger)
    : ActionBase<AccentColorSettings>
{
    private readonly ILogger<SwitchSystemAccentColorAction> _logger = logger;

    // 按行动集记录应用前的强调色状态，供恢复使用。
    private static readonly ConcurrentDictionary<Guid, AccentColorSnapshot> Snapshots = new();

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SwitchSystemAccentColorAction OnInvoke 开始");

        if (Settings == null)
        {
            return;
        }

        if (!WindowsAccentColor.TryParseHex(Settings.ColorHex, out var color))
        {
            _logger.LogWarning("系统强调色颜色格式无效：{ColorHex}", Settings.ColorHex);
            return;
        }

        try
        {
            if (IsRevertable)
            {
                Snapshots[ActionSet.Guid] = WindowsAccentColor.Capture();
            }

            WindowsAccentColor.Apply(color);
            _logger.LogInformation("系统强调色已切换为 {ColorHex}", Settings.ColorHex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换系统强调色失败");
            throw;
        }

        if (Settings.NotifyOnExecute)
        {
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已切换系统强调色", "\uE523", "")
            });
        }

        await base.OnInvoke();
        _logger.LogDebug("SwitchSystemAccentColorAction OnInvoke 完成");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();

        if (!Snapshots.TryRemove(ActionSet.Guid, out var snapshot))
        {
            _logger.LogInformation("未找到系统强调色快照，跳过恢复。ActionSet={ActionSetGuid}", ActionSet.Guid);
            return;
        }

        try
        {
            WindowsAccentColor.Restore(snapshot);
            _logger.LogInformation("已恢复系统强调色。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复系统强调色失败");
        }
    }
}

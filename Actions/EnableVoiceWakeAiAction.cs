using System;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.EnableVoiceWakeAi", "启用语音唤醒 AI", "\uED53", false)]
public class EnableVoiceWakeAiAction(ILogger<EnableVoiceWakeAiAction> logger) : ActionBase<EnableVoiceWakeAiActionSettings>
{
    private readonly ILogger<EnableVoiceWakeAiAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("EnableVoiceWakeAiAction OnInvoke 开始");

        if (Settings == null) return;

        var config = GlobalConstants.MainConfig?.Data;
        if (config == null) return;

        try
        {
            config.EnableVoiceWakeAi = Settings.Enable;
            var service = IAppHost.TryGetService<AiVoiceConversationService>();
            if (service == null)
            {
                config.EnableVoiceWakeAi = false;
                GlobalConstants.MainConfig?.Save();
                _logger.LogWarning("AI 服务尚未加载，无法{State}语音唤醒 AI", Settings.Enable ? "开启" : "关闭");
                return;
            }

            service.ApplyConfig();
            if (Settings.Enable && !service.IsWakeWordEnabled)
            {
                config.EnableVoiceWakeAi = false;
                GlobalConstants.MainConfig?.Save();
                _logger.LogWarning("语音唤醒 AI 未能启动：{Error}", service.LastError ?? "未知错误");
                return;
            }

            GlobalConstants.MainConfig?.Save();
            _logger.LogInformation("已{State}语音唤醒 AI 功能", Settings.Enable ? "开启" : "关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置语音唤醒 AI 功能失败");
            throw;
        }

        if (Settings.NotifyOnExecute)
        {
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    (Settings.Enable ? "已开启功能 " : "已关闭功能 ") + "启用语音唤醒 AI", "\uED53", "")
            });
        }

        await base.OnInvoke();
        _logger.LogDebug("EnableVoiceWakeAiAction OnInvoke 完成");
    }
}
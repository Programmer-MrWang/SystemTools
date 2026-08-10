using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.WakeUpVoiceConversationAi", "唤醒语音对话 AI", "\uEFF9", false)]
public class WakeUpVoiceConversationAiAction(
    ILogger<WakeUpVoiceConversationAiAction> logger,
    AiVoiceConversationService voiceConversationService) : ActionBase
{
    protected override async Task OnInvoke()
    {
        logger.LogInformation("正在通过行动唤醒语音对话 AI");

        if (!voiceConversationService.TryStartVoiceConversation())
        {
            logger.LogWarning("唤醒语音对话 AI 失败：{Error}", voiceConversationService.LastError ?? "未知错误");
        }

        await base.OnInvoke();
    }
}
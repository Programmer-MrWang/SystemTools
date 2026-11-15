using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools;

[ActionInfo("SystemTools.ShowAiChatDialog", "显示AI对话框", "", false)]
public sealed class NativeShowAiChatAction(NativeVoiceService voice) : ActionBase
{
    protected override Task OnInvoke() => voice.OpenAsync(listen: false);
}

[ActionInfo("SystemTools.WakeUpVoiceConversationAi", "唤醒语音对话 AI", "\uEFF9", false)]
public sealed class NativeWakeConversationAction(NativeVoiceService voice) : ActionBase
{
    protected override Task OnInvoke() => voice.OpenAsync();
}

[ActionInfo("SystemTools.EnableVoiceWakeAi", "启用语音唤醒 AI", "\uED53", false)]
public sealed class NativeEnableVoiceWakeAction(NativeVoiceService voice) : ActionBase<EnableVoiceWakeAiActionSettings>
{
    protected override Task OnInvoke()
    {
        GlobalConstants.MainConfig!.Data.EnableVoiceWakeAi = Settings.Enable;
        GlobalConstants.MainConfig.Save(); voice.ApplyConfig();
        return Task.CompletedTask;
    }
}

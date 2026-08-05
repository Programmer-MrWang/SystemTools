using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using SystemTools.Services;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo(
    "systemtools.settings.pluginDebug",
    "插件调试",
    "\uE2C8",
    "\uE2C8",
    true)]
public partial class PluginDebugSettingsPage : SettingsPageBase
{
    public PluginDebugSettingsPage()
    {
        InitializeComponent();
    }

    private void OnDebugVoiceWakeAiClick(object? sender, RoutedEventArgs e)
    {
        var service = IAppHost.TryGetService<AiVoiceConversationService>();
        if (service is null)
        {
            ShowSimpleMessage("无法调试语音唤醒 AI", "请先启用 AI 服务并重启 ClassIsland。");
            return;
        }

        if (!service.TryStartDebugConversation())
        {
            ShowSimpleMessage(
                "无法调试语音唤醒 AI",
                service.LastError ?? "请先选择 AI 模型，或等待当前语音对话结束。");
        }
    }

    private async void ShowSimpleMessage(string title, string message)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary
        };

        await dialog.ShowAsync(topLevel);
    }
}

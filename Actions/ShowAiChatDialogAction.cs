using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ShowAiChatDialog", "显示AI对话框", "\uEFFF", false)]
public class ShowAiChatDialogAction(
    ILogger<ShowAiChatDialogAction> logger,
    AiChatWindowService aiChatWindowService) : ActionBase
{
    protected override async Task OnInvoke()
    {
        logger.LogInformation("正在显示 AI 对话框");
        await aiChatWindowService.ShowAsync();
        await base.OnInvoke();
    }
}

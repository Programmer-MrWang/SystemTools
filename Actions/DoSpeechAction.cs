using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions
{
    [ActionInfo("SystemTools.DoSpeech", "语音播报", "\uE5C7", false)]
    public class DoSpeechAction(ILogger<DoSpeechAction> logger, ISpeechService speechService) : ActionBase<DoSpeechSettings>
    {
        private readonly ILogger<DoSpeechAction> _logger = logger;
        private readonly ISpeechService _speechService = speechService;

        protected override async Task OnInvoke()
        {
            _logger.LogDebug("DoSpeechAction OnInvoke 开始");
            var text = Settings?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogError("语音播报内容不能为空");
                throw new InvalidOperationException("语音播报内容不能为空");
            }
            else
            {
                _speechService.EnqueueSpeechQueue(text);
            }
            await base.OnInvoke();
            _logger.LogDebug("DoSpeechAction OnInvoke 完成");
        }
    }
}

using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.TriggerCustomTrigger", "触发指定触发器", "\uEAB7", false)]
public class TriggerCustomTriggerAction(ILogger<TriggerCustomTriggerAction> logger)
    : ActionBase<TriggerCustomTriggerSettings>
{
    private readonly ILogger<TriggerCustomTriggerAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("TriggerCustomTriggerAction OnInvoke 开始");

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.TriggerId))
        {
            _logger.LogWarning("触发器ID为空");
            return;
        }

        try
        {
            string? configDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(configDir))
            {
                _logger.LogError("无法获取程序运行位置");
                throw new FileNotFoundException($"无法获取程序运行位置");
            }

            var filePath = Path.Combine(configDir, "auto.json");

            _logger.LogInformation("正在将触发器ID写入: {Path}", filePath);

            var jsonContent = JsonSerializer.Serialize(new { TriggerId = Settings.TriggerId });
            await File.WriteAllTextAsync(filePath, jsonContent);

            _logger.LogInformation("触发器ID已写入: {TriggerId}", Settings.TriggerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写入auto.json失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("TriggerCustomTriggerAction OnInvoke 完成");
    }
}
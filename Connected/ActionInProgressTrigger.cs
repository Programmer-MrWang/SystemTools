using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Timers;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.ActionInProgressTrigger", "行动进行时", "\uEAB7")]
public class ActionInProgressTrigger : TriggerBase<ActionInProgressTriggerConfig>
{
    private readonly ILogger<ActionInProgressTrigger> _logger;
    private readonly string _autoJsonPath;
    private Timer? _checkTimer;

    public ActionInProgressTrigger(ILogger<ActionInProgressTrigger> logger)
    {
        _logger = logger;

        var configDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _autoJsonPath = Path.Combine(configDir, "auto.json");
    }

    public override void Loaded()
    {
        _checkTimer = new Timer(2000);
        _checkTimer.Elapsed += OnCheckTimer;
        _checkTimer.Start();
        _logger.LogDebug("行动进行时触发器已启动，每隔2秒检查 {Path}", _autoJsonPath);
    }

    public override void UnLoaded()
    {
        if (_checkTimer != null)
        {
            _checkTimer.Stop();
            _checkTimer.Dispose();
            _checkTimer = null;
        }
        _logger.LogDebug("行动进行时触发器已停止");
    }

    private void OnCheckTimer(object? sender, ElapsedEventArgs e)
    {
        try
        {
            if (!File.Exists(_autoJsonPath))
                return;

            if (string.IsNullOrWhiteSpace(Settings.TriggerId))
                return;

            string jsonContent;
            lock (this)
            {
                jsonContent = File.ReadAllText(_autoJsonPath);
            }

            using var doc = JsonDocument.Parse(jsonContent);
            if (!doc.RootElement.TryGetProperty("TriggerId", out var triggerIdElement))
                return;

            var triggerId = triggerIdElement.GetString();
            if (triggerId != Settings.TriggerId)
                return;

            lock (this)
            {
                File.Delete(_autoJsonPath);
            }

            _logger.LogInformation("检测到匹配的行动ID: {TriggerId}，触发执行", triggerId);
            Trigger();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查auto.json时发生错误");
        }
    }
}
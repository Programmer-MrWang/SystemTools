using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SystemTools.Services;
using SystemTools.Settings;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SimulateKeyCombination", "模拟组合键", "\uEA15", false)]
public class SimulateKeyCombinationAction(ILogger<SimulateKeyCombinationAction> logger) : ActionBase<KeyCombinationSettings>
{
    private readonly ILogger<SimulateKeyCombinationAction> _logger = logger;
    private const int KeyEventDelay = 20;
    private const int KeyHoldDelay = 40;
    private const int MaxKeyCount = 5;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SimulateKeyCombinationAction OnInvoke 开始");

        var keys = Settings?.Keys
            .Where(x => x.KeyCode is > 0)
            .Take(MaxKeyCount)
            .Select(x => x.KeyCode!.Value)
            .ToList() ?? [];

        if (keys.Count == 0)
        {
            _logger.LogWarning("没有录入的组合键按键");
            return;
        }

        var pressedKeys = new List<byte>();
        try
        {
            _logger.LogInformation("正在模拟同时按下 {Count} 个按键", keys.Count);

            foreach (var keyCode in keys)
            {
                PInvoke.keybd_event(keyCode, 0, 0, UIntPtr.Zero);
                pressedKeys.Add(keyCode);
                await Task.Delay(KeyEventDelay);
            }

            await Task.Delay(KeyHoldDelay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟组合键失败");
            throw;
        }
        finally
        {
            for (var i = pressedKeys.Count - 1; i >= 0; i--)
            {
                PInvoke.keybd_event(pressedKeys[i], 0, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
                await Task.Delay(KeyEventDelay);
            }
        }

        if (Settings?.NotifyOnExecute == true)
        {
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已完成模拟组合键", "\uE9FB", "")
            });
        }

        await base.OnInvoke();
        _logger.LogDebug("SimulateKeyCombinationAction OnInvoke 完成");
    }
}
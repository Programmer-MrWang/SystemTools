using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SystemTools.Settings;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SimulateKeyboard", "模拟键盘", "\uEA0F", false)]
public class SimulateKeyboardAction(ILogger<SimulateKeyboardAction> logger) : ActionBase<KeyboardInputSettings>
{
    private readonly ILogger<SimulateKeyboardAction> _logger = logger;
    private const int KEY_PRESS_DELAY = 20;
    private const int KEY_INTERVAL_DELAY = 100;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SimulateKeyboardAction OnInvoke 开始");

        if (Settings == null)
        {
            _logger.LogWarning("没有录制的按键");
            return;
        }

        var actions = Settings.Actions.Count > 0 ? Settings.Actions : ConvertLegacyKeys(Settings.Keys);
        if (actions.Count == 0)
        {
            _logger.LogWarning("没有录制的按键");
            return;
        }

        try
        {
            _logger.LogInformation("正在模拟 {Count} 个按键操作", actions.Count);

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                await Task.Delay((int)action.Interval);

                switch (action.Type)
                {
                    case KeyboardAction.ActionType.KeyDown:
                        PInvoke.keybd_event(action.KeyCode, 0, 0, UIntPtr.Zero);
                        break;
                    case KeyboardAction.ActionType.KeyUp:
                        PInvoke.keybd_event(action.KeyCode, 0,
                            Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;
                    default:
                        PInvoke.keybd_event(action.KeyCode, 0, 0, UIntPtr.Zero);
                        await Task.Delay(KEY_PRESS_DELAY);
                        PInvoke.keybd_event(action.KeyCode, 0,
                            Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
                        break;
                }
            }

            _logger.LogInformation("按键模拟完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟键盘失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("SimulateKeyboardAction OnInvoke 完成");
    }

    private static List<KeyboardAction> ConvertLegacyKeys(List<string>? keys)
    {
        var actions = new List<KeyboardAction>();
        if (keys == null)
        {
            return actions;
        }

        foreach (var key in keys)
        {
            var parts = key.Split(':', 2);
            if (!byte.TryParse(parts[0], out var keyCode))
            {
                continue;
            }

            actions.Add(new KeyboardAction
            {
                Type = KeyboardAction.ActionType.Press,
                KeyCode = keyCode,
                KeyName = parts.Length > 1 ? parts[1] : keyCode.ToString(),
                Interval = actions.Count == 0 ? 0 : KEY_INTERVAL_DELAY
            });
        }

        return actions;
    }

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    //private const uint KEYEVENTF_KEYUP = 0x0002;
}
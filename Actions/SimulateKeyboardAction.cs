using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
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

        if (Settings == null || Settings.Keys == null || Settings.Keys.Count == 0)
        {
            _logger.LogWarning("没有录制的按键");
            return;
        }

        try
        {
            _logger.LogInformation("正在模拟 {Count} 个按键", Settings.Keys.Count);

            for (int i = 0; i < Settings.Keys.Count; i++)
            {
                if (byte.TryParse(Settings.Keys[i].Split(':')[0], out byte keyCode))
                {
                    PInvoke.keybd_event(keyCode, 0, 0, UIntPtr.Zero);
                    await Task.Delay(KEY_PRESS_DELAY);
                    PInvoke.keybd_event(keyCode, 0,
                        Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);

                    if (i < Settings.Keys.Count - 1)
                    {
                        await Task.Delay(KEY_INTERVAL_DELAY);
                    }
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

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    //private const uint KEYEVENTF_KEYUP = 0x0002;
}
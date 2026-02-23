using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.F11Key", "按下 F11 键", "\uEA0B", false)]
public class F11Action(ILogger<F11Action> logger) : ActionBase
{
    private readonly ILogger<F11Action> _logger = logger;

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_F11 = 0x7A;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在模拟按下 F11 键");

            PInvoke.keybd_event(VK_F11, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            PInvoke.keybd_event(VK_F11, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);

            _logger.LogInformation("F11 键已成功发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 F11 键失败");
            throw;
        }

        await base.OnInvoke();
    }
}
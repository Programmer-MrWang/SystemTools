using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.EscKey", "按下 Esc 键", "\uEA0B", false)]
public class EscAction(ILogger<EscAction> logger) : ActionBase
{
    private readonly ILogger<EscAction> _logger = logger;

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_ESCAPE = 0x1B;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在模拟按下 Esc 键");

            PInvoke.keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            PInvoke.keybd_event(VK_ESCAPE, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP,
                UIntPtr.Zero);

            _logger.LogInformation("Esc 键已成功发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 Esc 键失败");
            throw;
        }

        await base.OnInvoke();
    }
}
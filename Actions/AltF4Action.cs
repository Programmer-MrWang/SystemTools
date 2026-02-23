using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.AltF4", "按下 Alt+F4", "\uEA0B", false)]
public class AltF4Action(ILogger<AltF4Action> logger) : ActionBase
{
    private readonly ILogger<AltF4Action> _logger = logger;

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_MENU = 0x12; // Alt 键
    private const byte VK_F4 = 0x73;   // F4 键

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在模拟按下 Alt+F4");

            // 按下 Alt
            PInvoke.keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);

            // 按下 F4
            PInvoke.keybd_event(VK_F4, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);

            // 释放 F4
            PInvoke.keybd_event(VK_F4, 0,Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(20);

            // 释放 Alt
            PInvoke.keybd_event(VK_MENU, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);

            _logger.LogInformation("Alt+F4 已成功发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 Alt+F4 失败");
            throw;
        }

        await base.OnInvoke();
    }
}
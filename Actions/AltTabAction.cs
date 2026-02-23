using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.AltTab", "按下 Alt+Tab", "\uEA0B", false)]
public class AltTabAction : ActionBase
{
    private readonly ILogger<AltTabAction> _logger;

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_MENU = 0x12; // Alt 键
    private const byte VK_TAB = 0x09;  // Tab 键
    //private const uint KEYEVENTF_KEYUP = 0x0002;

    public AltTabAction(ILogger<AltTabAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在模拟按下 Alt+Tab");

            // 按下 Alt
            PInvoke.keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);

            // 按下 Tab
            PInvoke.keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);

            // 释放 Tab
            PInvoke.keybd_event(VK_TAB, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(20);

            // 释放 Alt
            PInvoke.keybd_event(VK_MENU, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);

            _logger.LogInformation("Alt+Tab 已成功发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 Alt+Tab 失败");
            throw;
        }

        await base.OnInvoke();
    }
}
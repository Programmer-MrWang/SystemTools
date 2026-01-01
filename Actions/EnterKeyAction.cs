using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.EnterKey", "按下 Enter 键", "\uEA0B", false)]
public class EnterKeyAction : ActionBase
{
    private readonly ILogger<EnterKeyAction> _logger;

    // Windows API 导入
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // 常量定义
    private const byte VK_RETURN = 0x0D; // Enter 键的虚拟键码
    private const uint KEYEVENTF_KEYUP = 0x0002; // 按键释放标志

    public EnterKeyAction(ILogger<EnterKeyAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在模拟按下 Enter 键");

            // 按下 Enter 键（按下事件）
            keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);

            // 短暂延迟确保按键被注册
            await Task.Delay(20);

            // 释放 Enter 键（释放事件）
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            _logger.LogInformation("Enter 键已成功发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 Enter 键失败");
            throw; // 让框架记录错误
        }

        await base.OnInvoke();
    }
}
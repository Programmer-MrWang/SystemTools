using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.EscKey", "按下 Esc 键", "\uEA0B", false)]
public class EscAction : ActionBase
{
    private readonly ILogger<EscAction> _logger;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_ESCAPE = 0x1B;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public EscAction(ILogger<EscAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在模拟按下 Esc 键");

            keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

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
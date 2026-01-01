using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.FullscreenClock", "沉浸式时钟", "\uE4D2", false)]
public class FullscreenClockAction : ActionBase
{
    private readonly ILogger<FullscreenClockAction> _logger;
    private const string ClockUrl = "https://clock.qqhkx.com/";

    public FullscreenClockAction(ILogger<FullscreenClockAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("FullscreenClockAction OnInvoke 开始");

        try
        {
            _logger.LogInformation("正在打开沉浸式时钟: {Url}", ClockUrl);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c start \"\" \"{ClockUrl}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);

            _logger.LogInformation("等待1.5秒后发送F11全屏键");

            await Task.Delay(1500);

            _logger.LogDebug("发送F11键");
            keybd_event(VK_F11, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            keybd_event(VK_F11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            _logger.LogInformation("F11全屏键已发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开沉浸式时钟失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("FullscreenClockAction OnInvoke 完成");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_F11 = 0x7A;
    private const uint KEYEVENTF_KEYUP = 0x0002;
}
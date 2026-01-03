/*using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ClickSimulation", "模拟点击", "\uE5C1", false)]
public class ClickSimulationAction : ActionBase<ClickSimulationSettings>
{
    private readonly ILogger<ClickSimulationAction> _logger;

    public ClickSimulationAction(ILogger<ClickSimulationAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ClickSimulationAction OnInvoke 开始");

        if (Settings == null || (Settings.X == 0 && Settings.Y == 0))
        {
            _logger.LogWarning("未录制有效的点击坐标");
            return;
        }

        try
        {
            if (!SetCursorPos(Settings.X, Settings.Y))
                throw new Exception($"SetCursorPos 失败，错误码: {Marshal.GetLastWin32Error()}");

            await Task.Delay(50);

            mouse_event(MOUSEEVENTF_LEFTDOWN, Settings.X, Settings.Y, 0, UIntPtr.Zero);
            await Task.Delay(20);
            mouse_event(MOUSEEVENTF_LEFTUP, Settings.X, Settings.Y, 0, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟鼠标点击失败");
            throw;
        }

        await base.OnInvoke();
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
}*/
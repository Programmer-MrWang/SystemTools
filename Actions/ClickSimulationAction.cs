using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ClickSimulation", "模拟点击", "\uE5C1",false)]
public class ClickSimulationAction : ActionBase
{
    private readonly ILogger<ClickSimulationAction> _logger;
    private readonly string _filePath;

    public ClickSimulationAction(ILogger<ClickSimulationAction> logger)
    {
        _logger = logger;
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "click.json");
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ClickSimulationAction OnInvoke 开始");

        var settings = await LoadSettingsAsync();
        if (settings == null || (settings.X == 0 && settings.Y == 0))
        {
            _logger.LogWarning("未录制有效的点击坐标");
            return;
        }

        try
        {
            if (!SetCursorPos(settings.X, settings.Y))
                throw new Exception($"SetCursorPos 失败，错误码: {Marshal.GetLastWin32Error()}");

            await Task.Delay(50);

            mouse_event(MOUSEEVENTF_LEFTDOWN, settings.X, settings.Y, 0, UIntPtr.Zero);
            await Task.Delay(20);
            mouse_event(MOUSEEVENTF_LEFTUP, settings.X, settings.Y, 0, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟鼠标点击失败");
            throw;
        }

        await base.OnInvoke();
    }

    private async Task<ClickSimulationSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<ClickSimulationSettings>(json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取 click.json 失败");
        }
        return null;
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
}
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.WindowOperation", "窗口操作", "\uF4B3", false)]
public class WindowOperationAction : ActionBase<WindowOperationSettings>
{
    private readonly ILogger<WindowOperationAction> _logger;

    public WindowOperationAction(ILogger<WindowOperationAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("WindowOperationAction OnInvoke 开始");

        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                _logger.LogWarning("未找到活动窗口");
                return;
            }

            _logger.LogInformation("正在对窗口 {Hwnd} 执行操作：{Operation}", hwnd, Settings.Operation);

            switch (Settings.Operation)
            {
                case "最大化":
                    ShowWindow(hwnd, SW_MAXIMIZE);
                    break;
                case "最小化":
                    ShowWindow(hwnd, SW_MINIMIZE);
                    break;
                case "向下还原":
                    ShowWindow(hwnd, SW_RESTORE);
                    break;
                case "关闭窗口":
                    PostMessage(hwnd, WM_CLOSE, UIntPtr.Zero, IntPtr.Zero);
                    break;
            }

            _logger.LogInformation("窗口操作已执行");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "窗口操作失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("WindowOperationAction OnInvoke 完成");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    private const int SW_MAXIMIZE = 3;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x10;
}
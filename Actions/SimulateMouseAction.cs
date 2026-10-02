using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;
using ClassIsland.Core.Models.Notification;
using SystemTools.Services;
using ClassIsland.Shared;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SimulateMouse", "模拟鼠标", "\uE5C1", false)]
public class SimulateMouseAction(ILogger<SimulateMouseAction> logger) : ActionBase<MouseInputSettings>
{
    private readonly ILogger<SimulateMouseAction> _logger = logger;
    private const int MOUSE_DELAY = 20;
    private const int SCROLL_DELAY = 50;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SimulateMouseAction OnInvoke 开始");
        var isLeftButtonDown = false;
        var isRightButtonDown = false;
        var isMiddleButtonDown = false;
        var lastMouseX = 0;
        var lastMouseY = 0;
        var cancellationToken = InterruptCancellationToken;

        if (Settings == null || Settings.Actions == null || Settings.Actions.Count == 0)
        {
            _logger.LogWarning("没有录制的鼠标操作");
            return;
        }

        var disableMouseDuringExecution = Settings.DisableMouseDuringExecution;
        try
        {
            if (disableMouseDuringExecution)
            {
                await ExecuteBatchFile("jinyongshubiao.bat", "禁用鼠标");
                await Task.Delay(2000, cancellationToken);
            }
            _logger.LogInformation("正在模拟 {Count} 个鼠标操作", Settings.Actions.Count);

            for (int i = 0; i < Settings.Actions.Count; i++)
            {
                var action = Settings.Actions[i];
                lastMouseX = action.X;
                lastMouseY = action.Y;

                await Task.Delay(checked((int)action.Interval), cancellationToken);

                switch (action.Type)
                {
                    case MouseAction.ActionType.LeftClick:
                        if (isLeftButtonDown)
                        {
                            PInvoke.mouse_event(
                                Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X,
                                action.Y, 0, UIntPtr.Zero);
                            isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY, cancellationToken);
                        }

                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY, cancellationToken);
                        isLeftButtonDown = true;
                        PInvoke.mouse_event(
                            Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, action.X,
                            action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY, cancellationToken);
                        PInvoke.mouse_event(
                            Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X,
                            action.Y, 0, UIntPtr.Zero);
                        isLeftButtonDown = false;
                        break;

                    case MouseAction.ActionType.RightClick:
                        if (isLeftButtonDown)
                        {
                            PInvoke.mouse_event(
                                Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X,
                                action.Y, 0, UIntPtr.Zero);
                            isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY, cancellationToken);
                        }

                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY, cancellationToken);
                        isRightButtonDown = true;
                        PInvoke.mouse_event(
                            Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, action.X,
                            action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY, cancellationToken);
                        PInvoke.mouse_event(
                            Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, action.X,
                            action.Y, 0, UIntPtr.Zero);
                        isRightButtonDown = false;
                        break;

                    case MouseAction.ActionType.MiddleClick:
                        if (isLeftButtonDown)
                        {
                            PInvoke.mouse_event(
                                Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X,
                                action.Y, 0, UIntPtr.Zero);
                            isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY, cancellationToken);
                        }

                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY, cancellationToken);
                        isMiddleButtonDown = true;
                        PInvoke.mouse_event(
                            Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, action.X,
                            action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY, cancellationToken);
                        PInvoke.mouse_event(
                            Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, action.X,
                            action.Y, 0, UIntPtr.Zero);
                        isMiddleButtonDown = false;
                        break;

                    case MouseAction.ActionType.Scroll:
                        if (isLeftButtonDown)
                        {
                            PInvoke.mouse_event(
                                Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X,
                                action.Y, 0, UIntPtr.Zero);
                            isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY, cancellationToken);
                        }

                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY, cancellationToken);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL,
                            0, 0, action.ScrollDelta, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.DragMove:
                        if (!isLeftButtonDown)
                        {
                            PInvoke.SetCursorPos(action.X, action.Y);
                            await Task.Delay(MOUSE_DELAY, cancellationToken);
                            isLeftButtonDown = true;
                            PInvoke.mouse_event(
                                Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN,
                                action.X, action.Y, 0, UIntPtr.Zero);
                        }
                        else
                        {
                            PInvoke.SetCursorPos(action.X, action.Y);
                            await Task.Delay(MOUSE_DELAY, cancellationToken);
                        }

                        if (action.IsDragEnd && isLeftButtonDown)
                        {
                            PInvoke.mouse_event(
                                Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X,
                                action.Y, 0, UIntPtr.Zero);
                            isLeftButtonDown = false;
                        }

                        break;
                }
            }

            if (isLeftButtonDown)
            {
                var lastAction = Settings.Actions[Settings.Actions.Count - 1];
                PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
                    lastAction.X, lastAction.Y, 0, UIntPtr.Zero);
                isLeftButtonDown = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟鼠标失败");
            throw;
        }
        finally
        {
            // A failure/cancellation can interrupt the interval between a
            // mouse-down and mouse-up event. Release every button tracked by
            // this invocation before restoring the mouse device.
            if (isLeftButtonDown)
            {
                try
                {
                    PInvoke.mouse_event(
                        Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
                        lastMouseX, lastMouseY, 0, UIntPtr.Zero);
                }
                catch (Exception releaseException)
                {
                    _logger.LogWarning(releaseException, "释放模拟鼠标左键失败");
                }
                isLeftButtonDown = false;
            }

            if (isRightButtonDown)
            {
                try
                {
                    PInvoke.mouse_event(
                        Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP,
                        lastMouseX, lastMouseY, 0, UIntPtr.Zero);
                }
                catch (Exception releaseException)
                {
                    _logger.LogWarning(releaseException, "释放模拟鼠标右键失败");
                }
                isRightButtonDown = false;
            }

            if (isMiddleButtonDown)
            {
                try
                {
                    PInvoke.mouse_event(
                        Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP,
                        lastMouseX, lastMouseY, 0, UIntPtr.Zero);
                }
                catch (Exception releaseException)
                {
                    _logger.LogWarning(releaseException, "释放模拟鼠标中键失败");
                }
                isMiddleButtonDown = false;
            }

            if (disableMouseDuringExecution)
            {
                await Task.Delay(1000);
                await ExecuteBatchFile("huifu.bat", "启用鼠标");
            }
        }
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已结束自动操作自动化", "\uE9FB", "")
            });


        await base.OnInvoke();
        _logger.LogDebug("SimulateMouseAction OnInvoke 完成");
    }

    private async Task ExecuteBatchFile(string batchFileName, string operation)
    {
        try
        {
            string? pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
            if (string.IsNullOrEmpty(pluginDir))
            {
                _logger.LogError("无法获取程序集位置");
                throw new FileNotFoundException($"无法获取程序集位置");
            }

            var batchPath = Path.Combine(pluginDir, batchFileName);

            if (!File.Exists(batchPath))
            {
                _logger.LogWarning("找不到{Operation}批处理文件: {Path}", operation, batchPath);
                return;
            }

            _logger.LogInformation("正在运行{Operation}批处理: {Path}", operation, batchPath);

            var psi = new ProcessStartInfo
            {
                FileName = batchPath,
                WorkingDirectory = pluginDir,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi) ?? throw new Exception("无法启动批处理进程");
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogInformation("{Operation}批处理执行完成，退出码: {ExitCode}", operation, process.ExitCode);
            if (!string.IsNullOrWhiteSpace(output))
                _logger.LogDebug("批处理输出: {Output}", output);
            if (!string.IsNullOrWhiteSpace(error))
                _logger.LogWarning("批处理错误: {Error}", error);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("{Operation}批处理返回非零退出码: {ExitCode}", operation, process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行{Operation}批处理失败", operation);
        }
    }

    //[DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
    //[DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    //private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    //private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    //private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    //private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    //private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    //private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    //private const uint MOUSEEVENTF_WHEEL = 0x0800;
}

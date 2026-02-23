using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SimulateMouse", "模拟鼠标", "\uE5C1", false)]
public class SimulateMouseAction(ILogger<SimulateMouseAction> logger) : ActionBase<MouseInputSettings>
{
    private readonly ILogger<SimulateMouseAction> _logger = logger;
    private const int MOUSE_DELAY = 20;
    private const int SCROLL_DELAY = 50;
    private bool _isLeftButtonDown = false;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SimulateMouseAction OnInvoke 开始");
        _isLeftButtonDown = false;

        if (Settings == null || Settings.Actions == null || Settings.Actions.Count == 0)
        {
            _logger.LogWarning("没有录制的鼠标操作");
            return;
        }

        if (Settings.DisableMouseDuringExecution)
        {
            await ExecuteBatchFile("jinyongshubiao.bat", "禁用鼠标");
            await Task.Delay(2000);
        }

        try
        {
            _logger.LogInformation("正在模拟 {Count} 个鼠标操作", Settings.Actions.Count);

            for (int i = 0; i < Settings.Actions.Count; i++)
            {
                var action = Settings.Actions[i];

                await Task.Delay((int)action.Interval);

                switch (action.Type)
                {
                    case MouseAction.ActionType.LeftClick:
                        if (_isLeftButtonDown)
                        {
                            PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.RightClick:
                        if (_isLeftButtonDown)
                        {
                            PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, action.X, action.Y, 0, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.MiddleClick:
                        if (_isLeftButtonDown)
                        {
                            PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, action.X, action.Y, 0, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.Scroll:
                        if (_isLeftButtonDown)
                        {
                            PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        PInvoke.SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, 0, 0, action.ScrollDelta, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.DragMove:
                        if (!_isLeftButtonDown)
                        {
                            PInvoke.SetCursorPos(action.X, action.Y);
                            await Task.Delay(MOUSE_DELAY);
                            PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = true;
                        }
                        else
                        {
                            PInvoke.SetCursorPos(action.X, action.Y);
                            await Task.Delay(MOUSE_DELAY);
                        }

                        if (action.IsDragEnd && _isLeftButtonDown)
                        {
                            PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                        }
                        break;
                }
            }

            if (_isLeftButtonDown)
            {
                var lastAction = Settings.Actions[Settings.Actions.Count - 1];
                PInvoke.mouse_event(Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, lastAction.X, lastAction.Y, 0, UIntPtr.Zero);
                _isLeftButtonDown = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟鼠标失败");
            throw;
        }
        finally
        {
            if (Settings.DisableMouseDuringExecution)
            {
                await Task.Delay(1000);
                await ExecuteBatchFile("huifu.bat", "启用鼠标");
            }
        }

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
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SimulateMouse", "模拟鼠标", "\uE5C1", false)]
public class SimulateMouseAction : ActionBase<MouseInputSettings>
{
    private readonly ILogger<SimulateMouseAction> _logger;
    private const int MOUSE_DELAY = 20;
    private const int SCROLL_DELAY = 50;
    private bool _isLeftButtonDown = false;

    public SimulateMouseAction(ILogger<SimulateMouseAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SimulateMouseAction OnInvoke 开始");
        _isLeftButtonDown = false;

        if (Settings == null || Settings.Actions == null || Settings.Actions.Count == 0)
        {
            _logger.LogWarning("没有录制的鼠标操作");
            return;
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
                            mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY);
                        mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.RightClick:
                        if (_isLeftButtonDown)
                        {
                            mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY);
                        mouse_event(MOUSEEVENTF_RIGHTUP, action.X, action.Y, 0, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.MiddleClick:
                        if (_isLeftButtonDown)
                        {
                            mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        mouse_event(MOUSEEVENTF_MIDDLEDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                        await Task.Delay(MOUSE_DELAY);
                        mouse_event(MOUSEEVENTF_MIDDLEUP, action.X, action.Y, 0, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.Scroll:
                        if (_isLeftButtonDown)
                        {
                            mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                            await Task.Delay(MOUSE_DELAY);
                        }
                        SetCursorPos(action.X, action.Y);
                        await Task.Delay(MOUSE_DELAY);
                        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)action.ScrollDelta, UIntPtr.Zero);
                        break;

                    case MouseAction.ActionType.DragMove:
                        if (!_isLeftButtonDown)
                        {
                            SetCursorPos(action.X, action.Y);
                            await Task.Delay(MOUSE_DELAY);
                            mouse_event(MOUSEEVENTF_LEFTDOWN, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = true;
                        }
                        else
                        {
                            SetCursorPos(action.X, action.Y);
                            await Task.Delay(MOUSE_DELAY);
                        }

                        if (action.IsDragEnd && _isLeftButtonDown)
                        {
                            mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, UIntPtr.Zero);
                            _isLeftButtonDown = false;
                        }
                        break;
                }
            }

            if (_isLeftButtonDown)
            {
                var lastAction = Settings.Actions[Settings.Actions.Count - 1];
                mouse_event(MOUSEEVENTF_LEFTUP, lastAction.X, lastAction.Y, 0, UIntPtr.Zero);
                _isLeftButtonDown = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "模拟鼠标失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("SimulateMouseAction OnInvoke 完成");
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
}
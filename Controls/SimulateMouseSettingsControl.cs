using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SystemTools.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SystemTools.Controls;

public class SimulateMouseSettingsControl : ActionSettingsControlBase<MouseInputSettings>
{
    private Avalonia.Controls.Button _startButton;
    private Avalonia.Controls.ListBox _actionsListBox;
    private Avalonia.Controls.CheckBox _disableMouseCheckBox;
    private bool _isRecording;
    private HHOOK _mouseHookId = HHOOK.Null;
    private HHOOK _keyboardHookId = HHOOK.Null;
    private readonly List<MouseAction> _recordedActions = new();
    private readonly Stopwatch _stopwatch = new();
    private long _lastActionTime = 0;
    private bool _isDragging = false;
    private int _dragStartX = 0;
    private int _dragStartY = 0;
    private MouseAction? _lastDragAction = null;

    private HOOKPROC? _mouseHookProc;
    private HOOKPROC? _keyboardHookProc;

    //private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    public SimulateMouseSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "模拟鼠标操作（支持左键/右键/拖动/滚轮）",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        _startButton = new Avalonia.Controls.Button
        {
            Content = "开始录制鼠标行为",
            Width = 200,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        _startButton.Click += (s, e) => ToggleRecording();

        panel.Children.Add(_startButton);

        _actionsListBox = new Avalonia.Controls.ListBox
        {
            Height = 200,
            Margin = new(0, 10, 0, 0)
        };
        panel.Children.Add(_actionsListBox);

        _disableMouseCheckBox = new Avalonia.Controls.CheckBox
        {
            Content = "运行期间禁用鼠标",
            Margin = new(0, 10, 0, 0)
        };
        _disableMouseCheckBox.IsCheckedChanged += (s, e) =>
        {
            Settings.DisableMouseDuringExecution = _disableMouseCheckBox.IsChecked ?? false;
        };
        panel.Children.Add(_disableMouseCheckBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (Settings.Actions != null)
        {
            _recordedActions.Clear();
            _recordedActions.AddRange(Settings.Actions);
            UpdateListBox();
        }
        _disableMouseCheckBox.IsChecked = Settings.DisableMouseDuringExecution;
    }

    private void ToggleRecording()
    {
        if (!_isRecording)
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        _recordedActions.Clear();
        _lastActionTime = 0;
        _lastDragAction = null;
        _stopwatch.Restart();
        UpdateListBox();

        _startButton.Content = "正在录制...按下 Esc 停止";
        _startButton.IsEnabled = false;

        _mouseHookProc = MouseHookCallback;
        _keyboardHookProc = KeyboardHookCallback;

        var moduleHandle = PInvoke.GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName ?? "");
        _mouseHookId = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseHookProc, (HINSTANCE)moduleHandle.DangerousGetHandle(), 0);
        _keyboardHookId = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyboardHookProc, (HINSTANCE)moduleHandle.DangerousGetHandle(), 0);
    }

    private void StopRecording()
    {
        _isRecording = false;
        _stopwatch.Stop();

        if (_lastDragAction != null)
        {
            _lastDragAction.IsDragEnd = true;
            _lastDragAction = null;
        }

        _startButton.Content = "开始录制鼠标行为";
        _startButton.IsEnabled = true;

        if (_mouseHookId != IntPtr.Zero)
        {
            PInvoke.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = HHOOK.Null;
        }

        if (_keyboardHookId != IntPtr.Zero)
        {
            PInvoke.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = HHOOK.Null;
        }

        _mouseHookProc = null;
        _keyboardHookProc = null;

        Settings.Actions = new List<MouseAction>(_recordedActions);
    }

    private LRESULT MouseHookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var hookStruct = Marshal.PtrToStructure<Msllhookstruct>(lParam);
            var currentTime = _stopwatch.ElapsedMilliseconds;
            var interval = _lastActionTime == 0 ? 0 : currentTime - _lastActionTime;

            switch ((uint)wParam)
            {
                case WM_LBUTTONDOWN:
                    _isDragging = true;
                    _dragStartX = hookStruct.Pt.X;
                    _dragStartY = hookStruct.Pt.Y;
                    if (_lastDragAction != null)
                    {
                        _lastDragAction.IsDragEnd = true;
                        _lastDragAction = null;
                    }
                    break;

                case WM_LBUTTONUP:
                    if (_isDragging && (Math.Abs(hookStruct.Pt.X - _dragStartX) > 5 || Math.Abs(hookStruct.Pt.Y - _dragStartY) > 5))
                    {
                        AddAction(MouseAction.ActionType.DragMove, hookStruct.Pt.X, hookStruct.Pt.Y, 0, interval, true);
                        _lastDragAction = null;
                    }
                    else
                    {
                        AddAction(MouseAction.ActionType.LeftClick, hookStruct.Pt.X, hookStruct.Pt.Y, 0, interval);
                    }
                    _isDragging = false;
                    _lastActionTime = currentTime;
                    break;

                case WM_RBUTTONDOWN:
                    AddAction(MouseAction.ActionType.RightClick, hookStruct.Pt.X, hookStruct.Pt.Y, 0, interval);
                    _lastActionTime = currentTime;
                    break;

                case WM_MBUTTONDOWN:
                    AddAction(MouseAction.ActionType.MiddleClick, hookStruct.Pt.X, hookStruct.Pt.Y, 0, interval);
                    _lastActionTime = currentTime;
                    break;

                case WM_MOUSEWHEEL:
                    int delta = (short)((hookStruct.MouseData >> 16) & 0xFFFF);
                    AddAction(MouseAction.ActionType.Scroll, hookStruct.Pt.X, hookStruct.Pt.Y, delta, interval);
                    _lastActionTime = currentTime;
                    break;

                case WM_MOUSEMOVE:
                    if (_isDragging && interval > 50)
                    {
                        var action = AddAction(MouseAction.ActionType.DragMove, hookStruct.Pt.X, hookStruct.Pt.Y, 0, interval);
                        _lastDragAction = action;
                        _lastActionTime = currentTime;
                    }
                    break;
            }
        }

        return PInvoke.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private LRESULT KeyboardHookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var hookStruct = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            if (hookStruct.VkCode == 27 && wParam == 0x100)
            {
                Dispatcher.UIThread.Post(() => StopRecording());
            }
        }

        return PInvoke.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private MouseAction AddAction(MouseAction.ActionType type, int x, int y, int scrollDelta, long interval)
    {
        return AddAction(type, x, y, scrollDelta, interval, false);
    }

    private MouseAction AddAction(MouseAction.ActionType type, int x, int y, int scrollDelta, long interval, bool isDragEnd)
    {
        var action = new MouseAction
        {
            Type = type,
            X = x,
            Y = y,
            ScrollDelta = scrollDelta,
            Interval = interval,
            IsDragEnd = isDragEnd
        };

        _recordedActions.Add(action);
        _lastDragAction = isDragEnd ? null : _lastDragAction;

        Dispatcher.UIThread.Post(() =>
        {
            UpdateListBox();
        });

        return action;
    }

    private void UpdateListBox()
    {
        var items = new List<string>();
        for (int i = 0; i < _recordedActions.Count; i++)
        {
            var action = _recordedActions[i];
            string actionText = $"{i + 1}. [{action.Interval}ms] ";

            switch (action.Type)
            {
                case MouseAction.ActionType.LeftClick:
                    actionText += $"左键点击 ({action.X}, {action.Y})";
                    break;
                case MouseAction.ActionType.RightClick:
                    actionText += $"右键点击 ({action.X}, {action.Y})";
                    break;
                case MouseAction.ActionType.MiddleClick:
                    actionText += $"中键点击 ({action.X}, {action.Y})";
                    break;
                case MouseAction.ActionType.Scroll:
                    actionText += $"滚动 {action.ScrollDelta} ({action.X}, {action.Y})";
                    break;
                case MouseAction.ActionType.DragMove:
                    actionText += $"拖动 ({action.X}, {action.Y})" + (action.IsDragEnd ? " [结束]" : "");
                    break;
            }

            items.Add(actionText);
        }

        _actionsListBox.ItemsSource = items;
    }

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    //
    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern bool UnhookWindowsHookEx(IntPtr hHook);
    //
    //[DllImport("user32.dll")]
    //private static extern IntPtr CallNextHookEx(IntPtr hHook, int nCode, IntPtr wParam, IntPtr lParam);
    //
    //[DllImport("kernel32.dll")]
    //private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_MOUSEMOVE = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msllhookstruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Kbdllhookstruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }
}
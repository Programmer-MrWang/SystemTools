using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SystemTools.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SystemTools.Controls;

public class SimulateKeyboardSettingsControl : ActionSettingsControlBase<KeyboardInputSettings>
{
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly ListBox _actionsListBox;
    private readonly ComboBox _typeBox;
    private readonly TextBox _keyCodeBox;
    private readonly TextBox _keyNameBox;
    private readonly TextBox _intervalBox;
    private bool _isRecording;
    private HHOOK _hookId = HHOOK.Null;
    private readonly List<KeyboardAction> _recordedActions = [];
    private readonly Stopwatch _stopwatch = new();
    private long _lastActionTime;
    private HOOKPROC? _hookProc;

    public SimulateKeyboardSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };
        var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };

        _startButton = new Button { Content = "开始录制键盘输入", Width = 150 };
        _startButton.Click += (_, _) => StartRecording();
        _stopButton = new Button { Content = "结束录制", Width = 100, IsVisible = false };
        _stopButton.Click += (_, _) => StopRecording();
        buttonPanel.Children.Add(_startButton);
        buttonPanel.Children.Add(_stopButton);
        panel.Children.Add(buttonPanel);

        _actionsListBox = new ListBox { Height = 160 };
        _actionsListBox.SelectionChanged += (_, _) => LoadSelectedAction();
        panel.Children.Add(_actionsListBox);

        var editor = new StackPanel { Spacing = 6 };
        _typeBox = new ComboBox { ItemsSource = Enum.GetValues<KeyboardAction.ActionType>(), SelectedIndex = 0, MinWidth = 120 };
        _keyCodeBox = new TextBox { Watermark = "虚拟键码", Width = 90 };
        _keyNameBox = new TextBox { Watermark = "按键名称", Width = 120 };
        _intervalBox = new TextBox { Watermark = "延迟(ms)", Width = 90 };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = "操作", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        row.Children.Add(_typeBox);
        row.Children.Add(new TextBlock { Text = "键码", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        row.Children.Add(_keyCodeBox);
        row.Children.Add(new TextBlock { Text = "名称", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        row.Children.Add(_keyNameBox);
        row.Children.Add(new TextBlock { Text = "延迟", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        row.Children.Add(_intervalBox);
        editor.Children.Add(row);

        var editButtons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        AddButton(editButtons, "应用修改", ApplySelectedAction);
        AddButton(editButtons, "新增", AddActionFromEditor);
        AddButton(editButtons, "删除", DeleteSelectedAction);
        editor.Children.Add(editButtons);
        panel.Children.Add(editor);
        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _recordedActions.Clear();
        if (Settings.Actions.Count > 0)
        {
            _recordedActions.AddRange(Settings.Actions);
        }
        else
        {
            foreach (var key in Settings.Keys)
            {
                var parts = key.Split(':', 2);
                if (byte.TryParse(parts[0], out var keyCode))
                {
                    _recordedActions.Add(new KeyboardAction { KeyCode = keyCode, KeyName = parts.Length > 1 ? parts[1] : keyCode.ToString(), Interval = _recordedActions.Count == 0 ? 0 : 100 });
                }
            }
        }
        SaveActions();
        UpdateListBox();
    }

    private static void AddButton(StackPanel panel, string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private void StartRecording()
    {
        _isRecording = true;
        _recordedActions.Clear();
        _lastActionTime = 0;
        _stopwatch.Restart();
        UpdateListBox();
        _startButton.IsVisible = false;
        _stopButton.IsVisible = true;
        _hookProc = HookCallback;
        _hookId = (HHOOK)PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc,
            PInvoke.GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName), 0).DangerousGetHandle();
    }

    private void StopRecording()
    {
        _isRecording = false;
        _stopwatch.Stop();
        _startButton.IsVisible = true;
        _stopButton.IsVisible = false;
        if (_hookId != IntPtr.Zero)
        {
            PInvoke.UnhookWindowsHookEx(_hookId);
            _hookId = HHOOK.Null;
        }
        _hookProc = null;
        SaveActions();
    }

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _isRecording && wParam == 0x100)
        {
            var hookStruct = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            var currentTime = _stopwatch.ElapsedMilliseconds;
            var interval = _lastActionTime == 0 ? 0 : currentTime - _lastActionTime;
            _lastActionTime = currentTime;
            var keyName = ((System.Windows.Forms.Keys)hookStruct.VkCode).ToString();
            _recordedActions.Add(new KeyboardAction { Type = KeyboardAction.ActionType.Press, KeyCode = (byte)hookStruct.VkCode, KeyName = keyName, Interval = interval });
            Dispatcher.UIThread.Post(() => { SaveActions(); UpdateListBox(); });
        }
        return PInvoke.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void LoadSelectedAction()
    {
        if (_actionsListBox.SelectedIndex < 0 || _actionsListBox.SelectedIndex >= _recordedActions.Count) return;
        var action = _recordedActions[_actionsListBox.SelectedIndex];
        _typeBox.SelectedItem = action.Type;
        _keyCodeBox.Text = action.KeyCode.ToString();
        _keyNameBox.Text = action.KeyName;
        _intervalBox.Text = action.Interval.ToString();
    }

    private KeyboardAction? ReadEditor()
    {
        if (!byte.TryParse(_keyCodeBox.Text, out var keyCode) || !long.TryParse(_intervalBox.Text, out var interval)) return null;
        return new KeyboardAction { Type = (KeyboardAction.ActionType)(_typeBox.SelectedItem ?? KeyboardAction.ActionType.Press), KeyCode = keyCode, KeyName = string.IsNullOrWhiteSpace(_keyNameBox.Text) ? keyCode.ToString() : _keyNameBox.Text!, Interval = Math.Max(0, interval) };
    }

    private void ApplySelectedAction()
    {
        var action = ReadEditor();
        if (action == null || _actionsListBox.SelectedIndex < 0 || _actionsListBox.SelectedIndex >= _recordedActions.Count) return;
        _recordedActions[_actionsListBox.SelectedIndex] = action;
        SaveActions();
        UpdateListBox();
    }

    private void AddActionFromEditor()
    {
        var action = ReadEditor() ?? new KeyboardAction { Type = KeyboardAction.ActionType.Press, KeyCode = 13, KeyName = "Enter", Interval = 0 };
        _recordedActions.Add(action);
        SaveActions();
        UpdateListBox();
    }

    private void DeleteSelectedAction()
    {
        if (_actionsListBox.SelectedIndex < 0 || _actionsListBox.SelectedIndex >= _recordedActions.Count) return;
        _recordedActions.RemoveAt(_actionsListBox.SelectedIndex);
        SaveActions();
        UpdateListBox();
    }

    private void SaveActions()
    {
        Settings.Actions = [.. _recordedActions];
        Settings.Keys = _recordedActions.ConvertAll(x => $"{x.KeyCode}:{x.KeyName}");
    }

    private void UpdateListBox()
    {
        var items = new List<string>();
        for (var i = 0; i < _recordedActions.Count; i++)
        {
            var a = _recordedActions[i];
            items.Add($"{i + 1}. [{a.Interval}ms] {a.Type} {a.KeyName} ({a.KeyCode})");
        }
        _actionsListBox.ItemsSource = items;
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

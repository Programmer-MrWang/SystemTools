using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SystemTools.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SystemTools.Controls;

public class SimulateKeyboardSettingsControl : ActionSettingsControlBase<KeyboardInputSettings>
{
    private Avalonia.Controls.Button _startButton;
    private Avalonia.Controls.Button _stopButton;
    private Avalonia.Controls.TextBox _keysTextBox;
    private bool _isRecording;
    private HHOOK _hookId = HHOOK.Null;
    private readonly List<string> _recordedKeys = [];
    private HOOKPROC _hookProc;

    //private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    public SimulateKeyboardSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        var buttonPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };

        _startButton = new Avalonia.Controls.Button
        {
            Content = "开始录制键盘输入",
            Width = 150
        };
        _startButton.Click += (s, e) => StartRecording();

        _stopButton = new Avalonia.Controls.Button
        {
            Content = "结束录制",
            Width = 100,
            IsVisible = false
        };
        _stopButton.Click += (s, e) => StopRecording();

        buttonPanel.Children.Add(_startButton);
        buttonPanel.Children.Add(_stopButton);
        panel.Children.Add(buttonPanel);

        _keysTextBox = new Avalonia.Controls.TextBox
        {
            Height = 100,
            IsReadOnly = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Watermark = "录制的按键将显示在这里"
        };
        panel.Children.Add(_keysTextBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (Settings.Keys != null)
        {
            _recordedKeys.Clear();
            _recordedKeys.AddRange(Settings.Keys);
            UpdateTextBox();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        _recordedKeys.Clear();
        UpdateTextBox();

        _startButton.IsVisible = false;
        _stopButton.IsVisible = true;
        _keysTextBox.Watermark = "正在录制...";

        _hookProc = HookCallback;
        _hookId = (HHOOK)SetHook(_hookProc);
    }

    private void StopRecording()
    {
        _isRecording = false;

        _startButton.IsVisible = true;
        _stopButton.IsVisible = false;
        _keysTextBox.Watermark = "录制的按键将显示在这里";

        PInvoke.UnhookWindowsHookEx(_hookId);
        _hookId = HHOOK.Null;
        _hookProc = null;

        Settings.Keys = [.. _recordedKeys];
    }

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var hookStruct = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            var keyCode = hookStruct.VkCode;

            if (wParam == 0x100)
            {
                var keyName = ((System.Windows.Forms.Keys)keyCode).ToString();
                _recordedKeys.Add($"{keyCode}:{keyName}");

                Dispatcher.UIThread.Post(() =>
                {
                    UpdateTextBox();
                });
            }
        }

        return PInvoke.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void UpdateTextBox()
    {
        var sb = new StringBuilder();
        foreach (var key in _recordedKeys)
        {
            var parts = key.Split(':');
            if (parts.Length > 1)
            {
                sb.Append(parts[1]).Append(" ");
            }
        }
        _keysTextBox.Text = sb.ToString().Trim();
    }

    private IntPtr SetHook(HOOKPROC proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        return PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, proc, PInvoke.GetModuleHandle(curModule.ModuleName), 0).DangerousGetHandle();
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

    //private const int WH_KEYBOARD_LL = 13;

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
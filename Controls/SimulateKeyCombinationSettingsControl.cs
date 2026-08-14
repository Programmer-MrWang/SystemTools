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

public class SimulateKeyCombinationSettingsControl : ActionSettingsControlBase<KeyCombinationSettings>
{
    private const int MinKeyRows = 2;
    private const int MaxKeyRows = 5;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;

    private readonly StackPanel _keysPanel;
    private readonly Button _addButton;
    private readonly CheckBox _notifyCheckBox;
    private readonly List<KeyCombinationKey> _keySlots = [];
    private bool _isRecording;
    private int? _recordingIndex;
    private HHOOK _hookId = HHOOK.Null;
    private HOOKPROC? _hookProc;

    public SimulateKeyCombinationSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "设置要同时按下的按键。点击“录入”后按下一个键即可保存。"
        });

        _keysPanel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_keysPanel);

        _addButton = new Button { Content = "添加更多键" };
        _addButton.Click += (_, _) => AddKeySlot();
        panel.Children.Add(_addButton);

        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (_, _) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;
        _keySlots.Clear();
        _keySlots.AddRange(Settings.Keys);
        NormalizeKeySlots();
        SaveKeys();
        RenderRows();
    }

    private void AddKeySlot()
    {
        if (_keySlots.Count >= MaxKeyRows)
        {
            return;
        }

        _keySlots.Add(new KeyCombinationKey());
        SaveKeys();
        RenderRows();
    }

    private void DeleteKeySlot(int index)
    {
        if (index < MinKeyRows || index >= _keySlots.Count)
        {
            return;
        }

        if (_recordingIndex == index)
        {
            StopRecording();
        }

        _keySlots.RemoveAt(index);
        NormalizeKeySlots();
        SaveKeys();
        RenderRows();
    }

    private void StartRecording(int index)
    {
        if (index < 0 || index >= _keySlots.Count)
        {
            return;
        }

        StopRecording(false);
        _isRecording = true;
        _recordingIndex = index;
        _hookProc = HookCallback;
        _hookId = (HHOOK)PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc,
            PInvoke.GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName), 0).DangerousGetHandle();
        if (_hookId == IntPtr.Zero)
        {
            _isRecording = false;
            _recordingIndex = null;
            _hookProc = null;
        }

        RenderRows();
    }

    private void StopRecording(bool renderRows = true)
    {
        _isRecording = false;
        _recordingIndex = null;
        if (_hookId != IntPtr.Zero)
        {
            PInvoke.UnhookWindowsHookEx(_hookId);
            _hookId = HHOOK.Null;
        }

        _hookProc = null;
        if (renderRows)
        {
            RenderRows();
        }
    }

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0 && _isRecording && (wParam == WmKeyDown || wParam == WmSysKeyDown) && _recordingIndex is { } index)
        {
            var hookStruct = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            if (hookStruct.VkCode <= byte.MaxValue)
            {
                _isRecording = false;
                var keyCode = (byte)hookStruct.VkCode;
                var keyName = GetKeyName(hookStruct.VkCode);
                Dispatcher.UIThread.Post(() => CompleteRecording(index, keyCode, keyName));
            }
        }

        return PInvoke.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void CompleteRecording(int index, byte keyCode, string keyName)
    {
        if (index >= 0 && index < _keySlots.Count)
        {
            _keySlots[index] = new KeyCombinationKey
            {
                KeyCode = keyCode,
                KeyName = keyName
            };
            SaveKeys();
        }

        StopRecording();
    }

    private void NormalizeKeySlots()
    {
        while (_keySlots.Count < MinKeyRows)
        {
            _keySlots.Add(new KeyCombinationKey());
        }

        if (_keySlots.Count > MaxKeyRows)
        {
            _keySlots.RemoveRange(MaxKeyRows, _keySlots.Count - MaxKeyRows);
        }
    }

    private void SaveKeys()
    {
        Settings.Keys = [.. _keySlots];
    }

    private void RenderRows()
    {
        _keysPanel.Children.Clear();
        for (var i = 0; i < _keySlots.Count; i++)
        {
            var index = i;
            var key = _keySlots[index];
            var row = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8
            };

            row.Children.Add(new TextBlock
            {
                Text = $"按键 {index + 1}",
                Width = 60,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            row.Children.Add(new TextBox
            {
                Text = string.IsNullOrWhiteSpace(key.KeyName) ? string.Empty : $"{key.KeyName} ({key.KeyCode})",
                PlaceholderText = "未录入",
                Width = 180,
                IsReadOnly = true
            });

            var recordButton = new Button
            {
                Content = _recordingIndex == index ? "录入中…" : "录入",
                IsEnabled = !_isRecording || _recordingIndex == index
            };
            recordButton.Click += (_, _) => StartRecording(index);
            row.Children.Add(recordButton);

            if (index >= MinKeyRows)
            {
                var deleteButton = new Button { Content = "删除", IsEnabled = !_isRecording };
                deleteButton.Click += (_, _) => DeleteKeySlot(index);
                row.Children.Add(deleteButton);
            }

            _keysPanel.Children.Add(row);
        }

        _addButton.IsEnabled = _keySlots.Count < MaxKeyRows && !_isRecording;
    }

    private static string GetKeyName(uint vkCode)
    {
        return vkCode switch
        {
            0x10 or 0xA0 or 0xA1 => "Shift",
            0x11 or 0xA2 or 0xA3 => "Ctrl",
            0x12 or 0xA4 or 0xA5 => "Alt",
            0x5B or 0x5C => "Win",
            _ => ((System.Windows.Forms.Keys)vkCode).ToString()
        };
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
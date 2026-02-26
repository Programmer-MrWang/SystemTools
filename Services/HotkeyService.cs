using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SystemTools.Services;

public class HotkeyService : IHotkeyService, IDisposable
{
    private readonly ILogger<HotkeyService> _logger;
    private readonly HotkeyWindow _hotkeyWindow;
    private int _nextHotkeyId = 0x9000;
    private readonly Dictionary<int, HotkeyInfo> _registeredHotkeys = new();
    private readonly object _lock = new();

    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += (s, e) =>
        {
            if (_registeredHotkeys.TryGetValue(e.HotkeyId, out var info))
            {
                HotkeyPressed?.Invoke(this, new HotkeyEventArgs
                {
                    ModifierKeys = info.ModifierKeys,
                    VirtualKey = info.VirtualKey,
                    HotkeyId = e.HotkeyId
                });
            }
        };
    }

    public int RegisterHotkey(int modifierKeys, uint virtualKey)
    {
        lock (_lock)
        {
            // 检查是否已存在相同热键
            foreach (var kvp in _registeredHotkeys)
            {
                if (kvp.Value.ModifierKeys == modifierKeys && kvp.Value.VirtualKey == virtualKey)
                {
                    _logger.LogInformation("热键 {Hotkey} 已存在，复用已有热键 ID: {Id}",
                        GetHotkeyDisplay(modifierKeys, virtualKey), kvp.Key);
                    kvp.Value.RefCount++;
                    return kvp.Key;
                }
            }

            // 注册新热键
            var id = _nextHotkeyId++;
            var display = GetHotkeyDisplay(modifierKeys, virtualKey);

            try
            {
                if (_hotkeyWindow.Handle == IntPtr.Zero)
                {
                    _hotkeyWindow.CreateHandle(new CreateParams());
                }

                if (!PInvoke.RegisterHotKey(new HWND(_hotkeyWindow.Handle), id, (HOT_KEY_MODIFIERS)(uint)modifierKeys,
                        virtualKey))
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, $"注册热键 {display} 失败");
                }

                _registeredHotkeys[id] = new HotkeyInfo
                {
                    Id = id,
                    ModifierKeys = modifierKeys,
                    VirtualKey = virtualKey,
                    Display = display,
                    RefCount = 1
                };

                _logger.LogInformation("热键 {Hotkey} 注册成功，ID: {Id}", display, id);
                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册热键 {Hotkey} 失败", display);
                throw;
            }
        }
    }

    public void UnregisterHotkey(int hotkeyId)
    {
        lock (_lock)
        {
            if (!_registeredHotkeys.TryGetValue(hotkeyId, out var info))
                return;

            info.RefCount--;

            if (info.RefCount <= 0)
            {
                PInvoke.UnregisterHotKey(new HWND(_hotkeyWindow.Handle), hotkeyId);
                _registeredHotkeys.Remove(hotkeyId);
                _logger.LogInformation("热键 {Hotkey} (ID: {Id}) 已注销", info.Display, hotkeyId);
            }
            else
            {
                _logger.LogDebug("热键 {Hotkey} (ID: {Id}) 引用计数减至 {Count}",
                    info.Display, hotkeyId, info.RefCount);
            }
        }
    }

    public bool IsHotkeyRegistered(int modifierKeys, uint virtualKey)
    {
        lock (_lock)
        {
            foreach (var info in _registeredHotkeys.Values)
            {
                if (info.ModifierKeys == modifierKeys && info.VirtualKey == virtualKey)
                    return true;
            }

            return false;
        }
    }

    private readonly Dictionary<int, HotkeyInfo> _suspendedHotkeys = new();

    /// <summary>
    /// 临时挂起所有热键（用于录制新热键）
    /// </summary>
    public void SuspendAllHotkeys()
    {
        lock (_lock)
        {
            _suspendedHotkeys.Clear();
            foreach (var kvp in _registeredHotkeys.ToList())
            {
                PInvoke.UnregisterHotKey(new HWND(_hotkeyWindow.Handle), kvp.Key);
                _suspendedHotkeys[kvp.Key] = kvp.Value;
                _logger.LogDebug("临时注销热键: {Hotkey}", kvp.Value.Display);
            }

            _registeredHotkeys.Clear();
        }
    }

    /// <summary>
    /// 恢复所有热键注册
    /// </summary>
    public void ResumeAllHotkeys()
    {
        lock (_lock)
        {
            foreach (var kvp in _suspendedHotkeys)
            {
                var info = kvp.Value;
                if (PInvoke.RegisterHotKey(new HWND(_hotkeyWindow.Handle), kvp.Key,
                        (HOT_KEY_MODIFIERS)(uint)info.ModifierKeys, info.VirtualKey))
                {
                    _registeredHotkeys[kvp.Key] = info;
                    _logger.LogDebug("恢复热键注册: {Hotkey}", info.Display);
                }
                else
                {
                    _logger.LogError("恢复热键注册失败: {Hotkey}", info.Display);
                }
            }

            _suspendedHotkeys.Clear();
        }
    }

    public string GetHotkeyDisplay(int modifierKeys, uint virtualKey)
    {
        var parts = new List<string>();

        if ((modifierKeys & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifierKeys & 0x0001) != 0) parts.Add("Alt");
        if ((modifierKeys & 0x0004) != 0) parts.Add("Shift");
        if ((modifierKeys & 0x0008) != 0) parts.Add("Win");

        parts.Add(VirtualKeyToString(virtualKey));
        return string.Join("+", parts);
    }

    private string VirtualKeyToString(uint vk)
    {
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x30 => "0",
            0x31 => "1",
            0x32 => "2",
            0x33 => "3",
            0x34 => "4",
            0x35 => "5",
            0x36 => "6",
            0x37 => "7",
            0x38 => "8",
            0x39 => "9",
            0x41 => "A",
            0x42 => "B",
            0x43 => "C",
            0x44 => "D",
            0x45 => "E",
            0x46 => "F",
            0x47 => "G",
            0x48 => "H",
            0x49 => "I",
            0x4A => "J",
            0x4B => "K",
            0x4C => "L",
            0x4D => "M",
            0x4E => "N",
            0x4F => "O",
            0x50 => "P",
            0x51 => "Q",
            0x52 => "R",
            0x53 => "S",
            0x54 => "T",
            0x55 => "U",
            0x56 => "V",
            0x57 => "W",
            0x58 => "X",
            0x59 => "Y",
            0x5A => "Z",
            0x60 => "Num0",
            0x61 => "Num1",
            0x62 => "Num2",
            0x63 => "Num3",
            0x64 => "Num4",
            0x65 => "Num5",
            0x66 => "Num6",
            0x67 => "Num7",
            0x68 => "Num8",
            0x69 => "Num9",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            _ => $"0x{vk:X2}"
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var id in _registeredHotkeys.Keys)
            {
                PInvoke.UnregisterHotKey(new HWND(_hotkeyWindow.Handle), id);
            }

            _registeredHotkeys.Clear();
            _hotkeyWindow.Dispose();
        }
    }

    private class HotkeyInfo
    {
        public int Id { get; set; }
        public int ModifierKeys { get; set; }
        public uint VirtualKey { get; set; }
        public string Display { get; set; } = "";
        public int RefCount { get; set; }
    }

    private class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs
                {
                    HotkeyId = (int)m.WParam
                });
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }

    private class HotkeyPressedEventArgs : EventArgs
    {
        public int HotkeyId { get; set; }
    }
}
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.HotkeyTrigger", "按下F9时", "\uEA0B")]
public class HotkeyTrigger : TriggerBase<HotkeyTriggerConfig>
{
    private readonly ILogger<HotkeyTrigger> _logger;
    private readonly HotkeyWindow _hotkeyWindow;

    public HotkeyTrigger(ILogger<HotkeyTrigger> logger)
    {
        _logger = logger;
        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
    }

    public override void Loaded()
    {
        try
        {
            _hotkeyWindow.RegisterHotkey();
            _logger.LogInformation("F9热键注册成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册F9热键失败");
            throw;
        }
    }

    public override void UnLoaded()
    {
        _hotkeyWindow.UnregisterHotkey();
        _logger.LogDebug("F9热键已注销");
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (DateTime.Now - Settings.LastTriggered < TimeSpan.FromMilliseconds(500))
            return;

        Settings.LastTriggered = DateTime.Now;
        _logger.LogInformation("检测到F9键按下，触发自动化");
        Trigger();
    }

    private class HotkeyWindow : NativeWindow
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x9000;
        private const uint VK_F9 = 0x78;

        public event EventHandler? HotkeyPressed;

        public void RegisterHotkey()
        {
            CreateHandle(new CreateParams());

            if (!PInvoke.RegisterHotKey(new HWND(Handle), HOTKEY_ID, 0, VK_F9))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 0)
                {
                    throw new Win32Exception(error, "注册热键失败");
                }
            }
        }

        public void UnregisterHotkey()
        {
            PInvoke.UnregisterHotKey(new HWND(Handle), HOTKEY_ID);
            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }

            base.WndProc(ref m);
        }

        //[DllImport("user32.dll", SetLastError = true)]
        //private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        //
        //[DllImport("user32.dll", SetLastError = true)]
        //private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.TypeContent", "键入内容", "\uE4BE", false)]
public class TypeContentAction : ActionBase<TypeContentSettings>
{
    private readonly ILogger<TypeContentAction> _logger;

    public TypeContentAction(ILogger<TypeContentAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("OnInvoke 开始");

        if (string.IsNullOrWhiteSpace(Settings.Content))
        {
            _logger.LogWarning("内容为空或空白");
            return;
        }

        try
        {
            _logger.LogInformation("正在键入内容");

            SetClipboardText(Settings.Content);
            await Task.Delay(100);

            PInvoke.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            PInvoke.keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            PInvoke.keybd_event(VK_V, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(20);
            PInvoke.keybd_event(VK_CONTROL, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);

            _logger.LogInformation("内容已键入成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "键入内容失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("OnInvoke 完成");
    }

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    //
    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    //
    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern bool CloseClipboard();
    //
    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern bool EmptyClipboard();
    //
    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern IntPtr SetClipboardData(uint uFormat, IntPtr data);
    //
    //[DllImport("kernel32.dll", SetLastError = true)]
    //private static extern IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);
    //
    //[DllImport("kernel32.dll", SetLastError = true)]
    //private static extern IntPtr GlobalLock(IntPtr hMem);
    //
    //[DllImport("kernel32.dll", SetLastError = true)]
    //private static extern bool GlobalUnlock(IntPtr hMem);

    //[DllImport("kernel32.dll", SetLastError = true)]
    //private static extern IntPtr GlobalFree(IntPtr hMem);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    //private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint CF_UNICODETEXT = 13;
    //private const uint GMEM_MOVEABLE = 0x0002;

    private void SetClipboardText(string text)
    {
        if (!PInvoke.OpenClipboard(HWND.Null))
            throw new Exception($"无法打开剪贴板，错误码: {Marshal.GetLastWin32Error()}");

        try
        {
            PInvoke.EmptyClipboard();

            var bytes = (text.Length + 1) * 2;
            var hGlobal = PInvoke.GlobalAlloc(Windows.Win32.System.Memory.GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)bytes);
            if (hGlobal == IntPtr.Zero)
                throw new Exception($"无法分配内存，错误码: {Marshal.GetLastWin32Error()}");
            unsafe
            {
                var ptr = PInvoke.GlobalLock(hGlobal);
                if (ptr == null)
                {
                    PInvoke.GlobalFree(hGlobal);
                    throw new Exception($"无法锁定内存，错误码: {Marshal.GetLastWin32Error()}");
                }

                Marshal.Copy(text.ToCharArray(), 0, (nint)ptr, text.Length);
                Marshal.WriteInt16((nint)ptr, text.Length * 2, 0);

                PInvoke.GlobalUnlock(hGlobal);
                PInvoke.SetClipboardData(CF_UNICODETEXT, new HANDLE(hGlobal.Value));
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }
}
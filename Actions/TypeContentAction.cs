using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.TypeContent", "键入内容", "\uE4BE", false)]
public class TypeContentAction : ActionBase<TypeContentSettings>
{
    private readonly ILogger<TypeContentAction> _logger;
    private readonly string _filePath;

    public TypeContentAction(ILogger<TypeContentAction> logger)
    {
        _logger = logger;
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "type.json");
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("OnInvoke 开始");

        //string content = await LoadContentFromFile();

        if (string.IsNullOrWhiteSpace(Settings.Content))
        {
            _logger.LogWarning("内容为空或空白");
            return;
        }

        try
        {
            _logger.LogInformation("正在键入内容" );

            SetClipboardText(Settings.Content);
            await Task.Delay(100);

            // 模拟 Ctrl+V
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(20);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

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

    /*private async Task<string> LoadContentFromFile()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var settings = JsonSerializer.Deserialize<TypeContentSettings>(json);
                return settings?.Content ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取 type.json 文件失败");
        }
        return string.Empty;
    }*/

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, IntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private void SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
            throw new Exception($"无法打开剪贴板，错误码: {Marshal.GetLastWin32Error()}");

        try
        {
            EmptyClipboard();

            var bytes = (text.Length + 1) * 2;
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (IntPtr)bytes);
            if (hGlobal == IntPtr.Zero)
                throw new Exception($"无法分配内存，错误码: {Marshal.GetLastWin32Error()}");

            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hGlobal); 
                throw new Exception($"无法锁定内存，错误码: {Marshal.GetLastWin32Error()}");
            }

            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            Marshal.WriteInt16(ptr, text.Length * 2, 0);

            GlobalUnlock(hGlobal);
            SetClipboardData(CF_UNICODETEXT, hGlobal);
        }
        finally
        {
            CloseClipboard();
        }
    }
}
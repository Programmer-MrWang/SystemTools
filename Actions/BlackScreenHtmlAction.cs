using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.BlackScreenHtml", "黑屏html", "\uE643", false)]
public class BlackScreenHtmlAction(ILogger<BlackScreenHtmlAction> logger) : ActionBase
{
    private readonly ILogger<BlackScreenHtmlAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("BlackScreenHtmlAction OnInvoke 开始");

        try
        {
            string? pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
            if (string.IsNullOrEmpty(pluginDir))
            {
                _logger.LogError("无法获取程序集位置");
                throw new FileNotFoundException($"无法获取程序集位置");
            }

            var htmlPath = Path.Combine(pluginDir, "black.html");
            if (!File.Exists(htmlPath))
            {
                _logger.LogError("找不到 black.html 文件: {HtmlPath}", htmlPath);
                throw new FileNotFoundException($"找不到 black.html 文件: {htmlPath}");
            }

            _logger.LogInformation("正在打开 black.html: {HtmlPath}", htmlPath);

            // 调用默认浏览器打开
            var psi = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c start \"\" \"{htmlPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = pluginDir
            };

            Process.Start(psi);

            _logger.LogInformation("HTML文件已打开，等待1秒");

            await Task.Delay(1000);

            _logger.LogInformation("正在发送F11全屏键");

            //模拟F11按键
            PInvoke.keybd_event(VK_F11, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);
            PInvoke.keybd_event(VK_F11, 0, Windows.Win32.UI.Input.KeyboardAndMouse.KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP,
                UIntPtr.Zero);

            _logger.LogInformation("F11键已发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行黑屏html失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("BlackScreenHtmlAction OnInvoke 完成");
    }

    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_F11 = 0x7A;
    //private const uint KEYEVENTF_KEYUP = 0x0002;
}
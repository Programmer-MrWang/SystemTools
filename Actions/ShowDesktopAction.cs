using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using ClassIsland.Core.Models.Notification;
using SystemTools.Services;
using SystemTools.Settings;
using ClassIsland.Shared;
using System;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ShowDesktop", "显示桌面", "\uE62F", false)]
public class ShowDesktopAction(ILogger<ShowDesktopAction> logger) : ActionBase<ShortcutKeyNotificationSettings>
{
    private readonly ILogger<ShowDesktopAction> _logger = logger;

    private const byte VK_LWIN = 0x5B;
    private const byte VK_D = 0x44;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在模拟按下 Win+D 显示桌面");

            PInvoke.keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);

            PInvoke.keybd_event(VK_D, 0, 0, UIntPtr.Zero);
            await Task.Delay(20);

            PInvoke.keybd_event(VK_D, 0, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(20);

            PInvoke.keybd_event(VK_LWIN, 0, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, UIntPtr.Zero);
            _logger.LogInformation("Win+D 已成功发送");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 Win+D 失败");
            throw;
        }
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已执行显示桌面操作", "\uE9FB", "")
            });


        await base.OnInvoke();
    }
}

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using ClassIsland.Core.Models.Notification;
using SystemTools.Services;
using SystemTools.Settings;
using ClassIsland.Shared;

namespace SystemTools.Actions;

/// <summary>
/// 扩展屏幕
/// </summary>
[ActionInfo("SystemTools.ExtendDisplay", "扩展屏幕", "\uE647", false)]
public class ExtendDisplayAction(ILogger<ExtendDisplayAction> logger) : ActionBase<ShortcutKeyNotificationSettings>
{
    private readonly ILogger<ExtendDisplayAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在执行扩展屏幕命令");

            var processInfo = new ProcessStartInfo
            {
                FileName = "DisplaySwitch.exe",
                Arguments = "/extend",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                _logger.LogInformation("扩展屏幕命令已执行，退出码: {ExitCode}", process.ExitCode);

                if (!string.IsNullOrEmpty(error))
                    _logger.LogWarning("错误输出: {Error}", error);
            }
            else
            {
                throw new Exception("无法启动 DisplaySwitch.exe 进程");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "扩展屏幕失败");
            throw;
        }
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已执行扩展屏幕操作", "\uE9FB", "")
            });


        await base.OnInvoke();
    }
}
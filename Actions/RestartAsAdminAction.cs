using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.RestartAsAdmin", "重启应用为管理员身份", "\uEF53", false)]
public class RestartAsAdminAction(ILogger<RestartAsAdminAction> logger) : ActionBase
{
    private readonly ILogger<RestartAsAdminAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("RestartAsAdminAction OnInvoke 开始");

        try
        {
            if (IsRunningInAdmin())
            {
                _logger.LogInformation("当前已是管理员身份，无需重启");
                return;
            }

            var processStartInfo = new ProcessStartInfo()
            {
                FileName = Environment.ProcessPath?.Replace(".dll", ".exe"),
                Verb = "runas",
                UseShellExecute = true
            };

            processStartInfo.ArgumentList.Add("-m");

            var args = Environment.GetCommandLineArgs().ToList();
            args.RemoveAt(0);
            foreach (var i in args)
            {
                processStartInfo.ArgumentList.Add(i);
            }

            Process.Start(processStartInfo);
            AppBase.Current.Stop();

            _logger.LogInformation("已启动管理员权限实例并退出当前进程");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "以管理员身份重启失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("RestartAsAdminAction OnInvoke 完成");
    }

    private static bool IsRunningInAdmin()
    {
        var id = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(id);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
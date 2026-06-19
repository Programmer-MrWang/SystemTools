using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ImmediateShutdown", "立即关机", "\uEDE9", false)]
public class ImmediateShutdownAction(ILogger<ImmediateShutdownAction> logger) : ActionBase
{
    private readonly ILogger<ImmediateShutdownAction> _logger = logger;
    [DllImport("ntdll.dll", EntryPoint = "RtlAdjustPrivilege")] // 此API无法使用CsWin32包生成，因为这是个不公开的函数
    internal static extern uint W32_RtlAdjustPrivilege(int Privilege, bool bEnablePrivilege, bool IsThreadPrivilege, out bool PreviousValue);
    internal const int SE_SHUTDOWN_PRIVILEGE = 19;
    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ImmediateShutdownAction OnInvoke 开始");

        if (W32_RtlAdjustPrivilege(SE_SHUTDOWN_PRIVILEGE, true, false, out _) != 0x00000000) // STATUS_SUCCESS
        {
            _logger.LogError("执行立即关机失败:获取关机权限失败");
            throw new InvalidOperationException("执行立即关机失败");
        }
        if (!PInvoke.ExitWindowsEx(Windows.Win32.System.Shutdown.EXIT_WINDOWS_FLAGS.EWX_SHUTDOWN | Windows.Win32.System.Shutdown.EXIT_WINDOWS_FLAGS.EWX_POWEROFF, Windows.Win32.System.Shutdown.SHUTDOWN_REASON.SHTDN_REASON_FLAG_PLANNED))
        {
            _logger.LogError("执行立即关机失败");
            throw new InvalidOperationException("执行立即关机失败");
        }
        else { _logger.LogInformation("已执行立即关机命令"); }
        //try
        //{
        //    var psi = new ProcessStartInfo
        //    {
        //        FileName = "shutdown",
        //        Arguments = "-s -t 0",
        //        UseShellExecute = false,
        //        CreateNoWindow = true,
        //        WindowStyle = ProcessWindowStyle.Hidden
        //    };

        //    Process.Start(psi);
        //    _logger.LogInformation("已执行立即关机命令");
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogError(ex, "执行立即关机失败");
        //    throw;
        //}

        await base.OnInvoke();
        _logger.LogDebug("ImmediateShutdownAction OnInvoke 完成");
    }
}

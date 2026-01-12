/*using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SystemTools.Controls;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.AdvancedShutdown", "高级计时关机", "\uE4C4", false)]
public class AdvancedShutdownAction : ActionBase<AdvancedShutdownConfig>
{
    private readonly ILogger<AdvancedShutdownAction> _logger;
    private readonly ICommonDialogService _dialogService;

    public AdvancedShutdownAction(ILogger<AdvancedShutdownAction> logger)
    {
        _logger = logger;
        _dialogService = IAppHost.GetService<ICommonDialogService>()
            ?? throw new InvalidOperationException("无法获取ICommonDialogService");
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("AdvancedShutdownAction OnInvoke 开始");

        try
        {
            await CreateShutdownTaskAsync();
            await ShowShutdownDialogAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高级计时关机失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("AdvancedShutdownAction OnInvoke 完成");
    }

    private async Task CreateShutdownTaskAsync()
    {
        var shutdownTime = DateTime.Now.AddMinutes(2);
        var timeStr = shutdownTime.ToString("HH:mm");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c schtasks /create /tn \"SilentShutdown\" /tr \"shutdown /s /f /t 0\" /sc once /st {timeStr} /f",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("无法启动进程");

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("创建计划任务失败，退出码: {ExitCode}, 错误: {Error}", process.ExitCode, error);
            throw new Exception($"创建计划任务失败: {error}");
        }

        _logger.LogInformation("已创建2分钟后关机的计划任务");
    }

    private async Task ShowShutdownDialogAsync()
    {
        var shutdownTime = DateTime.Now.AddMinutes(2);
        var message = $"将在2分钟后关机（{shutdownTime:HH:mm}）……";

        var dialogControl = new ShutdownDialogControl(
            _dialogService,
            message,
            CancelShutdownAsync,
            ExtendShutdownAsync
        );

        await _dialogService.ShowCustomDialogAsync("高级计时关机", dialogControl);
    }

    public async Task CancelShutdownAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c schtasks /delete /tn \"SilentShutdown\" /f",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(psi);
        if (process == null) return;

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("取消关机失败，退出码: {ExitCode}, 错误: {Error}", process.ExitCode, error);
        }
        else
        {
            _logger.LogInformation("已取消关机计划");
        }
    }

    public async Task ExtendShutdownAsync()
    {
        await CancelShutdownAsync();

        var shutdownTime = DateTime.Now.AddMinutes(2);
        var timeStr = shutdownTime.ToString("HH:mm");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c schtasks /create /tn \"SilentShutdown\" /tr \"shutdown /s /f /t 0\" /sc once /st {timeStr} /f",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(psi);
        if (process == null) throw new Exception("无法启动进程");

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogError("延长关机失败，退出码: {ExitCode}, 错误: {Error}", process.ExitCode, error);
            throw new Exception($"延长关机失败: {error}");
        }

        _logger.LogInformation("已延长关机时间至2分钟后");
    }
}*/
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SystemTools.Services;

public class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger = logger;

    public async Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo,
        string operationName,
        IReadOnlyCollection<int>? successExitCodes = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        successExitCodes ??= new[] { 0 };
        timeout ??= TimeSpan.FromSeconds(30);

        _logger.LogInformation("[{Operation}] 启动进程: {FileName} {Arguments}",
            operationName, startInfo.FileName, startInfo.Arguments);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"[{operationName}] 无法启动进程: {startInfo.FileName}");

        var stdoutTask = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        var stderrTask = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout.Value);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process, operationName);
            throw new TimeoutException($"[{operationName}] 进程超时（>{timeout.Value.TotalSeconds:F0}s）: {startInfo.FileName}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var result = new ProcessRunResult(process.ExitCode, stdout, stderr);

        _logger.LogInformation("[{Operation}] 进程结束，退出码: {ExitCode}", operationName, result.ExitCode);

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            _logger.LogDebug("[{Operation}] 标准输出: {Output}", operationName, result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            _logger.LogWarning("[{Operation}] 标准错误: {Error}", operationName, result.StandardError);

        if (!successExitCodes.Contains(result.ExitCode))
        {
            throw new InvalidOperationException(
                $"[{operationName}] 进程执行失败，退出码: {result.ExitCode}，期望: {string.Join(",", successExitCodes)}");
        }

        return result;
    }

    private void TryKill(Process process, string operationName)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _logger.LogWarning("[{Operation}] 进程超时后已强制结束。", operationName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Operation}] 进程超时后结束失败。", operationName);
        }
    }
}

using System.Diagnostics;

namespace SystemTools.Common.Processes;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
public static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token = default, TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("命令未能启动。" );
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeout is { } limit) deadline.CancelAfter(limit);
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException ex)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (token.IsCancellationRequested) throw new TaskCanceledException("命令已中断。", ex, token);
            throw new TimeoutException("命令执行超时且已终止。", ex);
        }
        return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}

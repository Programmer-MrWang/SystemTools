using System;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.Common.Files;
using SystemTools.Common.Processes;

namespace SystemTools.Platforms.Windows;

public sealed class WindowsDirectoryCopyBackend : IDirectoryCopyBackend
{
    public async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        var result = await CommandRunner.RunAsync("robocopy.exe", [source, target, "/e", "/copyall", "/r:3", "/w:3", "/mt:4", "/nfl", "/ndl", "/np"], cancellationToken);
        if (result.ExitCode >= 8)
        {
            if ((result.StandardOutput + result.StandardError).Replace("\0", "").Contains("Manage Auditing", StringComparison.OrdinalIgnoreCase) ||
                result.StandardOutput.Contains("1314", StringComparison.Ordinal) ||
                result.StandardOutput.Contains("管理审核", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Robocopy /copyall 需要复制审计信息的权限；原版参数已保留，源和原目标未被覆盖。请使用具有相应权限的账户执行。");
            throw new System.IO.IOException($"Robocopy 复制失败，退出码：{result.ExitCode}。原目标仍保留。");
        }
    }
}

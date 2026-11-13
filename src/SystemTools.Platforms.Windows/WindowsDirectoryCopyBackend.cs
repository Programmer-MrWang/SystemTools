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
        if (result.ExitCode >= 8) throw new System.IO.IOException($"Robocopy 复制失败，退出码：{result.ExitCode}。原目标仍保留。");
    }
}

using ClassIsland.Core;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.Shared;

namespace SystemTools.Services;

public class ClassIslandMemoryAutoCleanupService(ILogger<ClassIslandMemoryAutoCleanupService> logger)
{
    private readonly ILogger<ClassIslandMemoryAutoCleanupService> _logger = logger;
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    private const long ThresholdBytes = 500L * 1024 * 1024;

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public void ApplyConfig()
    {
        var enabled = GlobalConstants.MainConfig?.Data.AutoCleanupClassIslandMemory == true;
        if (enabled)
        {
            Start();
            return;
        }

        Stop();
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_workerTask is { IsCompleted: false })
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? worker;
        lock (_sync)
        {
            cts = _cts;
            worker = _workerTask;
            _cts = null;
            _workerTask = null;
        }

        if (cts == null)
        {
            return;
        }

        try { cts.Cancel(); } catch { }
        cts.Dispose();

        if (worker != null)
        {
            try { worker.Wait(1000); } catch { }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                TryCleanupOnce();
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation.
        }
    }

    private void TryCleanupOnce()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();
            var privateMemory = process.PrivateMemorySize64;

            if (privateMemory <= ThresholdBytes)
            {
                return;
            }

            var before = GC.GetTotalMemory(true);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true);
            var after = GC.GetTotalMemory(true);

            _ = EmptyWorkingSet(process.Handle);

            _logger.LogInformation("ClassIsland 内存自动清理已执行。PrivateMemory={PrivateMemoryBytes}B ManagedBefore={ManagedBefore}B ManagedAfter={ManagedAfter}B", privateMemory, before, after);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ClassIsland 内存自动清理执行失败，将在下次周期继续。");
        }
    }
}

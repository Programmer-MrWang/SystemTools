using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.Shared;

namespace SystemTools.Services;

public sealed record SystemMemoryCleanupResult(
    bool Succeeded,
    bool RequiresAdministrator,
    int? BeforeMemoryLoadPercent,
    int? AfterMemoryLoadPercent,
    ulong AvailableMemoryIncreaseBytes,
    IReadOnlyList<string> Failures);

public sealed class SystemMemoryCleanupService(ILogger<SystemMemoryCleanupService> logger)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private const long MinimumAutoCleanupIntervalMilliseconds = 5 * 60 * 1000;
    private const int RearmMarginPercent = 5;

    private readonly ILogger<SystemMemoryCleanupService> _logger = logger;
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _workerTask;
    private long _lastCleanupTimestamp;

    public bool IsRunningAsAdministrator => WindowsSystemMemoryCleaner.IsRunningAsAdministrator();

    public void ApplyConfig()
    {
        var enabled = GlobalConstants.MainConfig?.Data.AutoCleanupSystemMemory == true;
        if (!enabled)
        {
            Stop();
            return;
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 3))
        {
            Stop();
            _logger.LogWarning("系统内存自动清理仅支持 Windows 8.1 及以上版本。");
            return;
        }

        if (!IsRunningAsAdministrator)
        {
            Stop();
            _logger.LogWarning("系统内存自动清理已启用，但当前 ClassIsland 未以管理员身份运行，本次运行不会执行清理。");
            return;
        }

        Start();
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_workerTask is { IsCompleted: false })
            {
                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _cancellationTokenSource = cancellationTokenSource;
            _workerTask = Task.Run(() => RunAsync(cancellationTokenSource.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellationTokenSource;
        Task? workerTask;

        lock (_lifecycleLock)
        {
            cancellationTokenSource = _cancellationTokenSource;
            workerTask = _workerTask;
            _cancellationTokenSource = null;
            _workerTask = null;
        }

        if (cancellationTokenSource == null)
        {
            return;
        }

        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (workerTask == null || workerTask.IsCompleted)
        {
            cancellationTokenSource.Dispose();
            return;
        }

        _ = DisposeCancellationSourceWhenCompletedAsync(workerTask, cancellationTokenSource);
    }

    public async Task<SystemMemoryCleanupResult> CleanupNowAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 3))
        {
            return new SystemMemoryCleanupResult(
                false,
                false,
                null,
                null,
                0,
                ["当前 Windows 版本不支持注册表缓存清理。"]);
        }

        if (!IsRunningAsAdministrator)
        {
            return new SystemMemoryCleanupResult(
                false,
                true,
                null,
                null,
                0,
                ["需要管理员权限。"]);
        }

        await _cleanupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteCleanupCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var autoCleanupArmed = true;

        try
        {
            using var timer = new PeriodicTimer(CheckInterval);
            while (true)
            {
                try
                {
                    autoCleanupArmed = await CheckAndCleanupAsync(autoCleanupArmed, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "系统内存自动清理检查失败，将在下个周期重试。");
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown or configuration change.
        }
    }

    private async Task<bool> CheckAndCleanupAsync(bool autoCleanupArmed, CancellationToken cancellationToken)
    {
        var config = GlobalConstants.MainConfig?.Data;
        if (config?.AutoCleanupSystemMemory != true)
        {
            return autoCleanupArmed;
        }

        if (!WindowsSystemMemoryCleaner.TryGetMemoryLoadPercent(out var memoryLoadPercent))
        {
            _logger.LogDebug("无法读取系统物理内存占用率，本轮自动清理检查已跳过。");
            return autoCleanupArmed;
        }

        var threshold = config.SystemMemoryCleanupThresholdPercent;
        var rearmThreshold = Math.Max(0, threshold - RearmMarginPercent);
        if (memoryLoadPercent < threshold)
        {
            if (memoryLoadPercent <= rearmThreshold)
            {
                return true;
            }

            return autoCleanupArmed;
        }

        if (!autoCleanupArmed || !HasAutoCleanupCooldownElapsed())
        {
            return autoCleanupArmed;
        }

        if (!await _cleanupLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return autoCleanupArmed;
        }

        SystemMemoryCleanupResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            config = GlobalConstants.MainConfig?.Data;
            if (config?.AutoCleanupSystemMemory != true
                || !WindowsSystemMemoryCleaner.TryGetMemoryLoadPercent(out memoryLoadPercent)
                || memoryLoadPercent < config.SystemMemoryCleanupThresholdPercent
                || !HasAutoCleanupCooldownElapsed())
            {
                return autoCleanupArmed;
            }

            cancellationToken.ThrowIfCancellationRequested();
            result = await ExecuteCleanupCoreAsync().ConfigureAwait(false);
            autoCleanupArmed = false;
        }
        finally
        {
            _cleanupLock.Release();
        }

        if (result.Succeeded)
        {
            _logger.LogInformation(
                "系统内存自动清理已执行。占用率 {BeforePercent}% -> {AfterPercent}%，可用内存增加 {AvailableIncreaseBytes}B。",
                result.BeforeMemoryLoadPercent,
                result.AfterMemoryLoadPercent,
                result.AvailableMemoryIncreaseBytes);
        }
        else
        {
            _logger.LogWarning(
                "系统内存自动清理未完全成功。失败项：{Failures}",
                string.Join("；", result.Failures));
        }

        return autoCleanupArmed;
    }

    private async Task<SystemMemoryCleanupResult> ExecuteCleanupCoreAsync()
    {
        try
        {
            return await Task.Run(WindowsSystemMemoryCleaner.Cleanup, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行系统内存原生清理时发生异常。");
            return new SystemMemoryCleanupResult(
                false,
                false,
                null,
                null,
                0,
                [$"原生清理异常：{ex.Message}"]);
        }
        finally
        {
            Interlocked.Exchange(ref _lastCleanupTimestamp, Environment.TickCount64);
        }
    }

    private bool HasAutoCleanupCooldownElapsed()
    {
        var lastCleanupTimestamp = Interlocked.Read(ref _lastCleanupTimestamp);
        return lastCleanupTimestamp == 0
               || Environment.TickCount64 - lastCleanupTimestamp >= MinimumAutoCleanupIntervalMilliseconds;
    }

    private static async Task DisposeCancellationSourceWhenCompletedAsync(
        Task workerTask,
        CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await workerTask.ConfigureAwait(false);
        }
        catch
        {
            // The worker logs unexpected failures itself.
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }
}

internal static class WindowsSystemMemoryCleaner
{
    private const int SystemMemoryListInformation = 80;
    private const int SystemFileCacheInformationEx = 81;
    private const int SystemRegistryReconciliationInformation = 155;

    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    private const uint TokenQuery = 0x0008;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint SePrivilegeEnabled = 0x00000002;

    private const string SeProfileSingleProcessName = "SeProfileSingleProcessPrivilege";
    private const string SeIncreaseQuotaName = "SeIncreaseQuotaPrivilege";

    public static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetMemoryLoadPercent(out int memoryLoadPercent)
    {
        if (TryGetMemoryStatus(out var status))
        {
            memoryLoadPercent = (int)status.MemoryLoad;
            return true;
        }

        memoryLoadPercent = 0;
        return false;
    }

    public static SystemMemoryCleanupResult Cleanup()
    {
        var failures = new List<string>();
        var hasBeforeStatus = TryGetMemoryStatus(out var beforeStatus);
        if (!hasBeforeStatus)
        {
            failures.Add($"读取清理前内存状态失败（Win32 {Marshal.GetLastWin32Error()}）");
        }

        nint tokenHandle = 0;
        PrivilegeAdjustment profilePrivilege = default;
        PrivilegeAdjustment quotaPrivilege = default;

        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenAdjustPrivileges, out tokenHandle))
        {
            failures.Add($"打开进程令牌失败（Win32 {Marshal.GetLastWin32Error()}）");
        }

        try
        {
            if (tokenHandle != 0)
            {
                if (!TryEnablePrivilege(tokenHandle, SeProfileSingleProcessName, out profilePrivilege, out var profileError))
                {
                    failures.Add($"启用 {SeProfileSingleProcessName} 失败（Win32 {profileError}）");
                }

                if (!TryEnablePrivilege(tokenHandle, SeIncreaseQuotaName, out quotaPrivilege, out var quotaError))
                {
                    failures.Add($"启用 {SeIncreaseQuotaName} 失败（Win32 {quotaError}）");
                }
            }

            var command = MemoryEmptyWorkingSets;
            AddNtStatusFailure(
                failures,
                "Working set",
                NtSetSystemInformationMemoryList(SystemMemoryListInformation, ref command, sizeof(int)));

            var fileCacheInformation = new SystemFileCacheInformation
            {
                MinimumWorkingSet = nuint.MaxValue,
                MaximumWorkingSet = nuint.MaxValue
            };
            AddNtStatusFailure(
                failures,
                "System file cache",
                NtSetSystemInformationFileCache(
                    SystemFileCacheInformationEx,
                    ref fileCacheInformation,
                    (uint)Marshal.SizeOf<SystemFileCacheInformation>()));

            command = MemoryPurgeLowPriorityStandbyList;
            AddNtStatusFailure(
                failures,
                "Standby list (without priority)",
                NtSetSystemInformationMemoryList(SystemMemoryListInformation, ref command, sizeof(int)));

            AddNtStatusFailure(
                failures,
                "Registry cache",
                NtSetSystemInformationEmpty(SystemRegistryReconciliationInformation, 0, 0));
        }
        finally
        {
            if (tokenHandle != 0)
            {
                if (!TryRestorePrivilege(tokenHandle, quotaPrivilege, out var quotaRestoreError))
                {
                    failures.Add($"恢复 {SeIncreaseQuotaName} 失败（Win32 {quotaRestoreError}）");
                }

                if (!TryRestorePrivilege(tokenHandle, profilePrivilege, out var profileRestoreError))
                {
                    failures.Add($"恢复 {SeProfileSingleProcessName} 失败（Win32 {profileRestoreError}）");
                }

                if (!CloseHandle(tokenHandle))
                {
                    failures.Add($"关闭进程令牌失败（Win32 {Marshal.GetLastWin32Error()}）");
                }
            }
        }

        var hasAfterStatus = TryGetMemoryStatus(out var afterStatus);
        if (!hasAfterStatus)
        {
            failures.Add($"读取清理后内存状态失败（Win32 {Marshal.GetLastWin32Error()}）");
        }

        var availableIncrease = hasBeforeStatus && hasAfterStatus && afterStatus.AvailablePhysical > beforeStatus.AvailablePhysical
            ? afterStatus.AvailablePhysical - beforeStatus.AvailablePhysical
            : 0;

        return new SystemMemoryCleanupResult(
            failures.Count == 0,
            false,
            hasBeforeStatus ? (int)beforeStatus.MemoryLoad : null,
            hasAfterStatus ? (int)afterStatus.MemoryLoad : null,
            availableIncrease,
            failures);
    }

    private static bool TryGetMemoryStatus(out MemoryStatusEx status)
    {
        status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        return GlobalMemoryStatusEx(ref status);
    }

    private static bool TryEnablePrivilege(
        nint tokenHandle,
        string privilegeName,
        out PrivilegeAdjustment adjustment,
        out int error)
    {
        adjustment = default;

        if (!LookupPrivilegeValue(null, privilegeName, out var luid))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        var requestedState = new TokenPrivileges
        {
            PrivilegeCount = 1,
            Privilege = new LuidAndAttributes
            {
                Luid = luid,
                Attributes = SePrivilegeEnabled
            }
        };

        Marshal.SetLastPInvokeError(0);
        if (!AdjustTokenPrivileges(
                tokenHandle,
                false,
                ref requestedState,
                (uint)Marshal.SizeOf<TokenPrivileges>(),
                out var previousState,
                out _))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        error = Marshal.GetLastWin32Error();
        if (error != 0)
        {
            return false;
        }

        adjustment = new PrivilegeAdjustment(previousState, previousState.PrivilegeCount != 0);
        return true;
    }

    private static bool TryRestorePrivilege(
        nint tokenHandle,
        PrivilegeAdjustment adjustment,
        out int error)
    {
        error = 0;
        if (!adjustment.ShouldRestore)
        {
            return true;
        }

        var previousState = adjustment.PreviousState;
        Marshal.SetLastPInvokeError(0);
        if (!AdjustTokenPrivileges(
                tokenHandle,
                false,
                ref previousState,
                (uint)Marshal.SizeOf<TokenPrivileges>(),
                out _,
                out _))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        error = Marshal.GetLastWin32Error();
        return error == 0;
    }

    private static void AddNtStatusFailure(List<string> failures, string operation, int status)
    {
        if (status < 0)
        {
            failures.Add($"{operation}（NTSTATUS 0x{unchecked((uint)status):X8}）");
        }
    }

    private readonly record struct PrivilegeAdjustment(TokenPrivileges PreviousState, bool ShouldRestore);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemFileCacheInformation
    {
        public nuint CurrentSize;
        public nuint PeakSize;
        public uint PageFaultCount;
        public nuint MinimumWorkingSet;
        public nuint MaximumWorkingSet;
        public nuint CurrentSizeIncludingTransitionInPages;
        public nuint PeakSizeIncludingTransitionInPages;
        public uint TransitionRePurposeCount;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privilege;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        nint tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        out TokenPrivileges previousState,
        out uint returnLength);

    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    private static extern int NtSetSystemInformationMemoryList(
        int systemInformationClass,
        ref int systemInformation,
        int systemInformationLength);

    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    private static extern int NtSetSystemInformationFileCache(
        int systemInformationClass,
        ref SystemFileCacheInformation systemInformation,
        uint systemInformationLength);

    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    private static extern int NtSetSystemInformationEmpty(
        int systemInformationClass,
        nint systemInformation,
        uint systemInformationLength);
}

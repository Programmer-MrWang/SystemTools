using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SystemTools.Services;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo,
        string operationName,
        IReadOnlyCollection<int>? successExitCodes = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

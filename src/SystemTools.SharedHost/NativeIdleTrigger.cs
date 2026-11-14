using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Enums;
using Microsoft.Extensions.Logging;
using SystemTools.Common.Automation;
using SystemTools.Contracts;
using SystemTools.Triggers;

namespace SystemTools;

[TriggerInfo("SystemTools.LongIdleTrigger", "长时间未操作电脑时", "\uE639")]
public sealed class NativeIdleTrigger(IUserIdle idle, NativeDesktopServices native, ILogger<NativeIdleTrigger> logger) : TriggerBase<LongIdleTriggerConfig>
{
    private CancellationTokenSource? _stop;
    private Task? _poll;

    public override void Loaded()
    {
        UnLoaded();
        _stop = CancellationTokenSource.CreateLinkedTokenSource(native.Stopping);
        _poll = PollAsync(_stop.Token);
    }
    public override void UnLoaded()
    {
        var stop = Interlocked.Exchange(ref _stop, null);
        stop?.Cancel();
        if (stop is not null) _ = (_poll ?? Task.CompletedTask).ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
    private async Task PollAsync(CancellationToken token)
    {
        var state = new IdleTriggerState();
        var failed = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    var sample = await Task.Run(idle.Read, token);
                    token.ThrowIfCancellationRequested();
                    var transition = state.Sample(sample, TimeSpan.FromMinutes(Math.Max(1, Settings.IdleMinutes)));
                    if (transition == IdleTransition.Trigger) Trigger();
                    if (transition == IdleTransition.Revert && AssociatedWorkflow?.ActionSet is { IsRevertEnabled: true, Status: ActionSetStatus.IsOn }) TriggerRevert();
                    failed = false;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    if (!failed) logger.LogWarning(error, "空闲检测暂不可用，恢复后继续采样。");
                    failed = true;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}

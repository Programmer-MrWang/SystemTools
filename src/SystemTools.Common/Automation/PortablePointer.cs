using SystemTools.Contracts;
namespace SystemTools.Common.Automation;

public sealed record PointerStep(int Kind, int X, int Y, int ScrollDelta, long Delay, bool DragEnd);
public static class PortablePointer
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPointerInput, SemaphoreSlim> Gates = new();
    public static async Task ExecuteAsync(IPointerInput input, IReadOnlyList<PointerStep> steps, CancellationToken token)
    {
        if (steps.Count > 10000 || steps.Any(x => x.Delay is < 0 or > 3600000 || x.Kind is < 0 or > 4))
            throw new ArgumentException("鼠标序列无效或过长。");
        var gate = Gates.GetValue(input, _ => new(1, 1)); await gate.WaitAsync(token);
        var held = new HashSet<int>(); var target = ""; var x = 0; var y = 0;
        try
        {
            token.ThrowIfCancellationRequested(); target = input.CaptureTarget();
            async Task Release()
            {
                foreach (var button in held.ToArray()) { await input.SendAsync("up", x, y, button, target, CancellationToken.None); held.Remove(button); }
            }
            foreach (var step in steps)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(step.Delay), token); x = step.X; y = step.Y;
                if (step.Kind != 4) await Release();
                await input.SendAsync(held.Contains(1) ? "drag" : "move", x, y, 0, target, token);
                if (step.Kind <= 2)
                {
                    var button = step.Kind switch { 0 => 1, 1 => 3, _ => 2 }; held.Add(button);
                    await input.SendAsync("down", x, y, button, target, token);
                    await Task.Delay(20, token); await Release();
                }
                else if (step.Kind == 3 && step.ScrollDelta != 0)
                    await input.SendAsync("scroll", x, y, Math.Clamp(step.ScrollDelta / 120 == 0 ? Math.Sign(step.ScrollDelta) : step.ScrollDelta / 120, -100, 100), target, token);
                else if (step.Kind == 4)
                {
                    if (!held.Contains(1)) { held.Add(1); await input.SendAsync("down", x, y, 1, target, token); }
                    if (step.DragEnd) await Release();
                }
            }
        }
        finally
        {
            List<Exception> failures = [];
            foreach (var button in held) try { await input.SendAsync("up", x, y, button, target, CancellationToken.None); } catch (Exception error) { failures.Add(error); }
            gate.Release();
            if (failures.Count > 0) throw new AggregateException("部分鼠标按键未释放，请检查系统输入状态。", failures);
        }
    }
}

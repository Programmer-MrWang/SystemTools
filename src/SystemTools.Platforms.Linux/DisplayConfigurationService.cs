using System.Text.Json;
using SystemTools.Common.Layout;
using SystemTools.Common.Media;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class DisplayConfigurationService : IDisplayConfiguration, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDisplayChange? _active;
    private bool _disposed;
    private static async Task<string> Run(CancellationToken token, params string[] args)
    {
        var result = await CommandRunner.RunAsync(MediaFiles.FindExecutable("xrandr") ?? throw new IOException("请安装 xrandr。"), args, token, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0) throw new IOException("X11 拒绝显示器配置，请检查连接和显示设置。");
        return result.StandardOutput;
    }
    private static async Task<IReadOnlyList<XrandrOutput>> Read(CancellationToken token) => XrandrLayout.Parse(await Run(token, "--query"));
    private static string Snapshot(IReadOnlyList<XrandrOutput> rows) => JsonSerializer.Serialize(rows.Select(x => new { x.Name, x.Primary, x.X, x.Y, x.Mode, x.Rate, x.Rotation }));
    public Capability Probe() => ProbeLayout(DisplayLayout.Extend);
    public Capability ProbeLayout(DisplayLayout layout)
    {
        try
        {
            using var connection = new X11Connection();
            XrandrLayout.ApplyArguments(Read(default).GetAwaiter().GetResult(), layout);
            return new(CapabilityState.Available);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or DllNotFoundException)
        { return new(CapabilityState.MissingDependency, "需要原生 X11、xrandr、至少两台兼容显示器及对应布局：" + error.Message); }
    }
    public async Task<IReadOnlyList<DesktopDisplay>> ListAsync(CancellationToken token) => (await Read(token)).Select(row =>
        new DesktopDisplay(row.Name, row.Name, row.BuiltIn, new(row.X, row.Y, row.Mode is null ? 0 : int.Parse(row.Mode.Split('x')[0]), row.Mode is null ? 0 : int.Parse(row.Mode.Split('x')[1])))).ToArray();
    public async Task<IDisplayChange> ApplyAsync(DisplayLayout layout, CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token)) throw new InvalidOperationException("另一个显示设置仍等待确认。");
        var leased = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var connection = new X11Connection();
            var verbose = await Run(token, "--verbose");
            foreach (System.Text.RegularExpressions.Match transform in System.Text.RegularExpressions.Regex.Matches(verbose, @"Transform:\s+([^\r\n]+)\r?\n\s+([^\r\n]+)\r?\n\s+([^\r\n]+)"))
            {
                var values = string.Join(" ", transform.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1).Select(x => x.Value)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var identity = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
                if (values.Length != 9 || values.Select((x, i) => double.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, out var value) && Math.Abs(value - identity[i]) < .00001).Any(x => !x))
                    throw new InvalidOperationException("请先在系统设置中关闭显示缩放变换，避免无法完整恢复原配置。");
            }
            if (verbose.Contains("panning ", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("当前显示使用平移区域，请在系统显示设置中调整。");
            var original = await Read(token);
            var arguments = XrandrLayout.ApplyArguments(original, layout);
            string expected;
            try
            {
                await Run(token, arguments);
                var current = await Read(token);
                var active = current.Where(x => x.Mode is not null).ToArray();
                var wanted = original.Where(x => layout == DisplayLayout.InternalOnly ? x.BuiltIn : layout == DisplayLayout.ExternalOnly ? !x.BuiltIn : true).Select(x => x.Name).ToHashSet();
                if (!active.Select(x => x.Name).ToHashSet().SetEquals(wanted)) throw new IOException("部分显示器未应用布局。");
                if (layout == DisplayLayout.InternalOnly && active.Any(x => !x.BuiltIn) ||
                    layout == DisplayLayout.ExternalOnly && active.Any(x => x.BuiltIn) || active.Length == 0 ||
                    layout == DisplayLayout.Mirror && active.Select(x => (x.Mode, x.X, x.Y)).Distinct().Count() != 1 ||
                    layout == DisplayLayout.Extend && active.Select(x => x.X).Distinct().Count() != active.Length)
                    throw new IOException("显示器未确认请求的布局。");
                expected = Snapshot(current);
            }
            catch { await Run(default, XrandrLayout.RestoreArguments(original)); throw; }
            _active = new TimedDisplayChange(async () =>
            {
                if (Snapshot(await Read(default)) != expected) throw new IOException("显示器状态已变化，无法确认。");
            }, async () =>
            {
                if (Snapshot(await Read(default)) != expected) throw new IOException("显示器已拔出或设置已被修改，已停止覆盖，请在系统设置恢复。");
                await Run(default, XrandrLayout.RestoreArguments(original));
                if (Snapshot(await Read(default)) != Snapshot(original)) throw new IOException("显示器未确认恢复，请检查系统设置。");
            }, () => _gate.Release());
            leased = true; return _active;
        }
        finally { if (!leased) _gate.Release(); }
    }
    public void Dispose() { _disposed = true; _active?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
}

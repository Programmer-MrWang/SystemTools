namespace SystemTools.Contracts;

/// <summary>Global desktop coordinates in logical points; frames carry their actual pixel dimensions.</summary>
public sealed record ScreenRegion(double X, double Y, double Width, double Height)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || !double.IsFinite(Height) ||
            Width <= 0 || Height <= 0 || Width > 8192 || Height > 8192)
            throw new ArgumentOutOfRangeException(nameof(Width), "请选择有效的屏幕区域（最大 8192 点）。");
    }
}

public sealed record BackgroundFrame(byte[] Png, ScreenRegion Region, int PixelWidth, int PixelHeight, bool ExcludesOwnProcess);
public interface IBackgroundCapture : INativeCapability
{
    Task<BackgroundFrame> CaptureAsync(ScreenRegion region, CancellationToken token);
}
public sealed record RecognizedText(ScreenRegion Bounds, double Confidence);
public interface ITextRecognition : INativeCapability
{
    Task<IReadOnlyList<RecognizedText>> RecognizeAsync(BackgroundFrame frame, CancellationToken token);
}

public enum DesktopMaterial { Solid, SystemMaterial, SystemGlass }
public sealed record MaterialResult(DesktopMaterial Applied, string Reason);
/// <summary>The handle is an Avalonia native view handle. The lease restores the original native hierarchy.</summary>
public interface IWindowMaterial : INativeCapability
{
    IDisposable Apply(nint view, DesktopMaterial material, Action<MaterialResult> applied);
}

public enum DisplayLayout { Mirror, Extend, InternalOnly, ExternalOnly }
public sealed record DesktopDisplay(string Id, string Name, bool BuiltIn, ScreenRegion Bounds);
public interface IDisplayConfiguration : INativeCapability
{
    Capability ProbeLayout(DisplayLayout layout);
    Task<IReadOnlyList<DesktopDisplay>> ListAsync(CancellationToken token);
    /// <summary>Returns a rollback lease. Dispose before confirmation restores the previous configuration.</summary>
    Task<IDisplayChange> ApplyAsync(DisplayLayout layout, CancellationToken token);
}
public interface IDisplayChange : IAsyncDisposable
{
    Task ConfirmAsync(CancellationToken token);
}

public enum BrightnessKind { BuiltInBacklight, ExternalDdc, SoftwareDimming }
public sealed record BrightnessDevice(string Id, string Name, BrightnessKind Kind, double Percent);
public interface IHardwareBrightness : INativeCapability
{
    Task<IReadOnlyList<BrightnessDevice>> ListAsync(CancellationToken token);
    Task<BrightnessDevice> SetAsync(string id, double percent, CancellationToken token);
}

public sealed class AdvancedDesktopServices : IDisposable
{
    public IBackgroundCapture? Background { get; init; }
    public ITextRecognition? TextRecognition { get; init; }
    public IWindowMaterial? Material { get; init; }
    public IDisplayConfiguration? Displays { get; init; }
    public IHardwareBrightness? Brightness { get; init; }
    private int _disposed;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        List<Exception> failures = [];
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var item in new object?[] { Background, TextRecognition, Material, Displays, Brightness }.OfType<IDisposable>())
            if (seen.Add(item))
                try { item.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("高级桌面资源清理失败。", failures);
    }
}

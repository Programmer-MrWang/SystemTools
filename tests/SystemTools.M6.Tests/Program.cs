using SystemTools.Contracts;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {++count}: {name}"); }
void Reject(Action action, string name) { try { action(); } catch (ArgumentOutOfRangeException) { Check(true, name); return; } throw new Exception(name); }
new ScreenRegion(-1920, -1080, 640, 480).Validate(); Check(true, "支持负坐标显示器");
Reject(() => new ScreenRegion(0, 0, double.NaN, 5).Validate(), "拒绝无效区域");
Reject(() => new ScreenRegion(0, 0, 8193, 5).Validate(), "限制捕获尺寸");
var resource = new Resource();
var bundle = new AdvancedDesktopServices { Background = resource, TextRecognition = resource };
bundle.Dispose(); bundle.Dispose(); Check(resource.Disposals == 1, "共享原生资源只释放一次");
var broken = new Resource { Fails = true }; var other = new Resource();
try { new AdvancedDesktopServices { Background = broken, TextRecognition = other }.Dispose(); } catch (AggregateException) { }
Check(other.Disposals == 1, "一个资源失败仍释放其余资源");
Check(BrightnessKind.BuiltInBacklight != BrightnessKind.SoftwareDimming && BrightnessKind.ExternalDdc != BrightnessKind.SoftwareDimming, "软件变暗与硬件亮度分离");

var policy = new SystemTools.Common.Layout.BackgroundThemePolicy();
Check(policy.Observe(0, 1) is null && policy.Observe(0, 1) == 2, "稳定两帧才切换主题");
Check(policy.Observe(128, 2) is null && policy.Observe(130, 2) is null, "迟滞带保持主题");
Check(policy.Observe(255, 2) is null && policy.Observe(0, 2) is null && policy.Observe(255, 2) is null, "交替噪声不会切换主题");
Check(policy.Observe(double.NaN, 2) is null, "拒绝无效亮度");

sealed class Resource : IBackgroundCapture, ITextRecognition, IDisposable
{
    public int Disposals; public bool Fails;
    public Capability Probe() => new(CapabilityState.Available);
    public Task<BackgroundFrame> CaptureAsync(ScreenRegion region, CancellationToken token) => throw new NotImplementedException();
    public Task<IReadOnlyList<RecognizedText>> RecognizeAsync(BackgroundFrame frame, CancellationToken token) => throw new NotImplementedException();
    public void Dispose() { Disposals++; if (Fails) throw new IOException(); }
}

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

var ocr = new SystemTools.Common.Layout.TextOcclusionPolicy();
var region = new ScreenRegion(0, 0, 200, 100);
RecognizedText[] hits = [new(new(10, 10, 80, 20), .9)];
Check(!ocr.Observe(region, hits) && ocr.Observe(region, hits), "连续两次命中才隐藏");
Check(ocr.Observe(region, []) && ocr.Observe(region, []) && !ocr.Observe(region, []), "隐藏后连续三次清空恢复");
Check(!ocr.Observe(region, [new(new(10, 10, 80, 20), .2)]) && !ocr.Observe(region, [new(new(220, 10, 80, 20), .99)]), "忽略低置信度和区域外文字");
ocr.Observe(region, hits); ocr.Observe(region, hits); ocr.Reset(); Check(!ocr.Hidden, "故障与停用清除隐藏状态");

var rollbacks = 0; var releases = 0;
var transaction = new SystemTools.Common.Layout.TimedDisplayChange(() => Task.CompletedTask, () => { rollbacks++; return Task.CompletedTask; }, () => releases++, TimeSpan.FromMilliseconds(30));
await transaction.Completion.WaitAsync(TimeSpan.FromSeconds(2)); await transaction.DisposeAsync();
Check(rollbacks == 1 && releases == 1, "显示事务超时仅回滚和释放一次");
var confirmed = new SystemTools.Common.Layout.TimedDisplayChange(() => Task.CompletedTask, () => { rollbacks++; return Task.CompletedTask; }, () => releases++);
await confirmed.ConfirmAsync(default); await confirmed.DisposeAsync();
Check(rollbacks == 1 && releases == 2, "显示确认后不回滚");
var failed = new SystemTools.Common.Layout.TimedDisplayChange(() => Task.CompletedTask, () => throw new IOException("rollback"), () => releases++);
try { await failed.DisposeAsync(); } catch (IOException) { }
Check(failed.Completion.IsFaulted && releases == 3, "回滚失败可观察并仍释放互斥");
var outputs = SystemTools.Common.Layout.XrandrLayout.Parse("eDP-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis)\n   1920x1080 60.00*+\nHDMI-1 connected 1920x1080+1920+0 (normal left inverted right x axis y axis)\n   1920x1080 60.00*+\n");
Check(outputs.Count == 2 && outputs[0].BuiltIn && outputs[1].X == 1920, "解析真实格式的 Xrandr 输出");
Check(SystemTools.Common.Layout.XrandrLayout.ApplyArguments(outputs, DisplayLayout.ExternalOnly).Contains("--off"), "单外屏明确关闭内屏");
Check(SystemTools.Common.Layout.XrandrLayout.RestoreArguments(outputs).Contains("60.00"), "显示恢复保留刷新率");

sealed class Resource : IBackgroundCapture, ITextRecognition, IDisposable
{
    public int Disposals; public bool Fails;
    public Capability Probe() => new(CapabilityState.Available);
    public Task<BackgroundFrame> CaptureAsync(ScreenRegion region, CancellationToken token) => throw new NotImplementedException();
    public Task<IReadOnlyList<RecognizedText>> RecognizeAsync(BackgroundFrame frame, CancellationToken token) => throw new NotImplementedException();
    public void Dispose() { Disposals++; if (Fails) throw new IOException(); }
}

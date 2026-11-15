using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemTools.Common.Layout;
using SystemTools.Contracts;

namespace SystemTools.M6.MacHostProbe;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var root = Environment.GetEnvironmentVariable("SystemTools_M6_ProbeRoot") ?? throw new InvalidOperationException("需要隔离验收目录。");
        AppBase.Current.AppStarted += async (_, _) =>
        {
            var checks = new List<string>(); var failures = new List<string>(); var pending = new List<string>();
            Window? overlay = null;
            void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
            try
            {
                var loaded = IPluginService.LoadedPlugins.Single(x => x.Manifest.Id == "SystemTools");
                if (Environment.GetEnvironmentVariable("SystemTools_M6_ProbeCase") == "missing-backend")
                    Check(loaded.LoadStatus == PluginLoadStatus.Error, "缺少本机模块时宿主明确拒绝插件");
                else
                {
                    Check(loaded.LoadStatus == PluginLoadStatus.Loaded, "原版宿主加载 M6 插件");
                    var provider = IAppHost.Host!.Services;
                    var native = provider.GetRequiredService<NativeDesktopServices>();
                    var advanced = native.Advanced;
                    var catalog = provider.GetRequiredService<FeatureCatalog>();
                    Check(PlatformFeaturePolicy.WindowsExclusive.All(id => !catalog.CanCreate(id)), "Mac 不注册 Windows 专属设备与提权入口");
                    var page = await Dispatcher.UIThread.InvokeAsync(() => ActivatorUtilities.CreateInstance<NativePermissionSettingsPage>(provider));
                    Check(page.Content is not null, "实际宿主创建全部原生能力设置页");
                    var displays = await advanced.Displays!.ListAsync(default);
                    Check(displays.Count > 0, "读取实际显示器拓扑");
                    Check(!advanced.Displays.ProbeLayout(DisplayLayout.InternalOnly).CanExecute && !advanced.Displays.ProbeLayout(DisplayLayout.ExternalOnly).CanExecute, "Mac 不冒充公共 API 可关闭单屏");
                    if (displays.Count < 2) pending.Add("本机未连接第二屏，镜像/扩展和热插拔待外屏验收");
                    var brightness = await advanced.Brightness!.ListAsync(default);
                    if (brightness.Count == 0) Check(!advanced.Brightness.Probe().CanExecute, "本机无可靠背光时明确不可用");
                    else pending.Add("硬件亮度只读枚举成功；写入待设备验收");
                    var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "fixture.json"))).RootElement;
                    var region = new ScreenRegion(fixture.GetProperty("x").GetDouble(), fixture.GetProperty("y").GetDouble(), fixture.GetProperty("width").GetDouble(), fixture.GetProperty("height").GetDouble());
                    var buttonHits = 0;
                    overlay = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var button = new Button { Name = "MaterialHit", Content = "SystemTools 材质点击验收", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                        button.Click += (_, _) => buttonHits++;
                        var window = new Window { Title = "SystemTools M6 材质验收", Width = region.Width, Height = region.Height,
                            WindowDecorations = WindowDecorations.None, Background = Brushes.Lime, Topmost = true, ShowInTaskbar = false, Content = button };
                        window.Position = new PixelPoint((int)region.X, (int)region.Y); window.Show(); return window;
                    });
                    await Task.Delay(500);
                    var capture = advanced.Background!;
                    if (capture.Probe().CanExecute)
                    {
                        var frame = await capture.CaptureAsync(region, default);
                        Check(frame.ExcludesOwnProcess && frame.Png.Length > 100 && frame.PixelWidth > 0 && frame.PixelHeight > 0, "真正背景捕获返回排除本进程的有效 PNG");
                        var redValue = NativeBackgroundAppearance.ReadLuminance(frame);
                        Check(redValue is > 40 and < 110, "绿色宿主遮挡层被排除，捕获到外部红色夹具：" + redValue);
                        var mapped = await ((IWindowRegionProvider)capture).WindowRegionAsync(overlay.TryGetPlatformHandle()!.Handle, new(0, 0, overlay.Width, overlay.Height), default);
                        Check(Math.Abs(mapped.X - region.X) < 2 && Math.Abs(mapped.Y - region.Y) < 2, "Avalonia 句柄转换为真实屏幕逻辑坐标");
                        async Task Paint(string mode)
                        {
                            File.WriteAllText(Path.Combine(root, "fixture-mode"), mode);
                            for (var i = 0; i < 30; i++)
                            {
                                await Task.Delay(100);
                                if (File.Exists(Path.Combine(root, "fixture-ack")) && File.ReadAllText(Path.Combine(root, "fixture-ack")) == mode) { await Task.Delay(300); return; }
                            }
                            throw new IOException("外部夹具没有确认绘制：" + mode);
                        }
                        await Paint("white");
                        var light = await capture.CaptureAsync(region, default);
                        var lightValue = NativeBackgroundAppearance.ReadLuminance(light);
                        Check(lightValue > 240, "真实白色背景亮度可读：" + lightValue);
                        await Paint("black");
                        Check(NativeBackgroundAppearance.ReadLuminance(await capture.CaptureAsync(region, default)) < 15, "真实黑色背景亮度可读");
                        await Paint("text");
                        var textFrame = await capture.CaptureAsync(region, default);
                        var boxes = await advanced.TextRecognition!.RecognizeAsync(textFrame, default);
                        Check(boxes.Any(x => x.Confidence >= .6), "Vision 从真实背景识别高置信度文本位置：" + JsonSerializer.Serialize(boxes));
                        var policy = new TextOcclusionPolicy();
                        Check(!policy.Observe(region, boxes) && policy.Observe(region, boxes), "真实背景文本触发稳定避让判定");
                        Check(policy.Observe(region, []) && policy.Observe(region, []) && !policy.Observe(region, []), "无文字后恢复判定不会因已隐藏而停止");
                        using var cancel = new CancellationTokenSource(); cancel.Cancel();
                        try { await capture.CaptureAsync(region, cancel.Token); throw new Exception("预取消仍进行了捕获"); } catch (OperationCanceledException) { checks.Add("预取消不进行背景捕获"); }
                    }
                    else pending.Add("原版宿主缺少屏幕录制权限，真实背景/OCR 待本人允许");
                    var material = provider.GetRequiredService<NativeWindowMaterial>();
                    await Dispatcher.UIThread.InvokeAsync(() => material.Update(overlay, 1));
                    Check(material.LastError is "macOS 原生玻璃" or "当前系统使用原生磨砂材质" || material.LastError.StartsWith("实色表面（尊重系统"), "Avalonia 窗口实际接入原生系统材质：" + material.LastError);
                    File.WriteAllText(Path.Combine(root, "material-ready"), "ready");
                    if (Environment.GetEnvironmentVariable("SystemTools_M6_ClickAcceptance") == "1")
                    {
                        for (var i = 0; i < 600 && buttonHits == 0; i++) await Task.Delay(100);
                        Check(buttonHits > 0, "原生玻璃下真实点击到达 Avalonia 按钮");
                    }
                    await Dispatcher.UIThread.InvokeAsync(() => material.Update(overlay, 0));
                    Check(true, "材质切回经典表面并释放租约");
                }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => overlay?.Close());
                File.WriteAllText(Path.Combine(root, "acceptance.json"), JsonSerializer.Serialize(new { passed = failures.Count == 0, checks, failures, pending, fullPlatformAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
                await Dispatcher.UIThread.InvokeAsync(() => AppBase.Current.Stop());
            }
        };
    }
}

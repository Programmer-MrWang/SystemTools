using System.Reflection;
using System.Text.Json;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemTools.Common.Media;
using SystemTools.Contracts;
using SystemTools.Settings;

namespace SystemTools.M5.MacHostProbe;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var root = Environment.GetEnvironmentVariable("SystemTools_M5_ProbeRoot")
            ?? throw new InvalidOperationException("This probe requires an explicit isolated sandbox.");
        EventHandler? started = null;
        started = async (_, _) =>
        {
            AppBase.Current.AppStarted -= started;
            var checks = new List<string>();
            var failures = new List<string>();
            var physical = new Dictionary<string, bool>();
            JsonElement? identity = null;
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            try
            {
                var loaded = IPluginService.LoadedPlugins.Single(item => item.Manifest.Id == "SystemTools");
                if (Environment.GetEnvironmentVariable("SystemTools_M5_ProbeCase") == "missing-backend")
                    Check(loaded.LoadStatus == PluginLoadStatus.Error, "Mac 后端缺失时宿主明确报告插件加载失败");
                else
                {
                    Check(loaded.LoadStatus == PluginLoadStatus.Loaded, "未改动宿主加载正式 SystemTools 插件");
                    var hostPlist = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "../Info.plist");
                    var text = File.ReadAllText(hostPlist);
                    Check(!text.Contains("NSCameraUsageDescription", StringComparison.Ordinal) && !text.Contains("NSMicrophoneUsageDescription", StringComparison.Ordinal),
                        "实际运行宿主没有相机或麦克风用途声明");
                    var provider = IAppHost.Host!.Services;
                    var camera = provider.GetRequiredService<CameraCaptureContext>().Camera;
                    var state = await ((IAsyncCapabilityProbe)camera).ProbeAsync(default);
                    Check(state.State != CapabilityState.MissingDependency, "宿主内插件通过 NSWorkspace 连接独立采集助手");
                    Check(IActionService.ActionInfos.ContainsKey("SystemTools.CameraCapture"), "相机行动在助手异步连接前已完成注册");
                    var catalog = provider.GetRequiredService<FeatureCatalog>();
                    Check(!state.CanExecute || catalog.Get("SystemTools.CameraCapture").State == CapabilityState.Available,
                        "助手连接后相机能力可立即使用，无需再次重启注册");
                    var client = camera.GetType().GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(camera)!;
                    identity = (JsonElement)client.GetType().GetProperty("Identity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
                    Check(identity.Value.GetProperty("bundleId").GetString() == "io.github.programmer-mrwang.systemtools.companion" && identity.Value.GetProperty("parentPid").GetInt32() == 1,
                        "宿主启动的采集助手有独立 bundle 身份并由 launchd 托管");
                    var page = await Dispatcher.UIThread.InvokeAsync(() => ActivatorUtilities.CreateInstance<MediaSettingsPage>(provider));
                    Check(page.Content is not null, "真实宿主可创建相机语音设置页");
                    async Task<JsonElement> Request(string operation, object? arguments = null) =>
                        await (Task<JsonElement>)client.GetType().GetMethod(operation.EndsWith(".probe", StringComparison.Ordinal) ? "CallAsync" : "RequireAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(client, [operation, arguments, CancellationToken.None, null])!;
                    var microphone = await Request("microphone.probe");
                    physical["camera"] = state.CanExecute;
                    physical["microphone"] = microphone.GetProperty("state").GetString() == "Available";
                    if (state.CanExecute)
                    {
                        var devices = await camera.ListDevicesAsync(default);
                        var folder = Path.Combine(root, "camera-action");
                        var action = provider.GetRequiredKeyedService<ActionBase>("SystemTools.CameraCapture");
                        var item = new ActionItem { Id = "SystemTools.CameraCapture", Settings = new CameraCaptureSettings { DeviceName = devices[0].Id, SaveFolder = folder } };
                        var set = new ActionSet(); set.ActionItems.Add(item); set.SetStartRunning(true);
                        try { await action.InvokeAsync(item, set, isRevertable: false); }
                        finally { set.SetEndRunning(true); }
                        using var bitmap = new Bitmap(Directory.EnumerateFiles(folder, "*.png").Single());
                        Check(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0 && item.Exception is null,
                            "未改动宿主中的正式抓拍行动生成可解码真实 PNG");
                    }
                    if (physical["microphone"])
                    {
                        var id = Guid.NewGuid().ToString("D");
                        await Request("microphone.prepare", new { id });
                        try
                        {
                            await Request("microphone.start", new { id });
                            await Task.Delay(500);
                            var audio = await Request("microphone.poll", new { id });
                            Check(Convert.FromBase64String(audio.GetProperty("pcm").GetString()!).Length > 0, "未改动宿主中的插件收到助手采集的真实 PCM");
                        }
                        finally { await Request("microphone.release", new { id }); }
                        if (Environment.GetEnvironmentVariable("SystemTools_M5_TestModel") is { } model && MediaFiles.IsVoskModel(model))
                        {
                            provider.GetRequiredService<MediaPreferencesStore>().Current.SpeechModelPath = model;
                            var media = provider.GetRequiredService<PlatformMediaServices>();
                            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                            var recognition = Task.Run(async () =>
                            {
                                try { await foreach (var segment in media.Speech.RecognizeAsync(stop.Token)) { } }
                                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                            });
                            try
                            {
                                var active = false;
                                for (var attempt = 0; attempt < 50 && !recognition.IsCompleted; attempt++)
                                {
                                    var running = await Request("session.status");
                                    if (running.GetProperty("microphoneJobs").GetInt32() == 1) { active = true; break; }
                                    await Task.Delay(100);
                                }
                                Check(active, "宿主正式语音服务加载 Vosk 并通过助手启动真实采音");
                                await Task.Delay(500);
                            }
                            finally { stop.Cancel(); await recognition; }
                            var ended = await Request("session.status");
                            Check(ended.GetProperty("microphoneJobs").GetInt32() == 0, "取消宿主正式识别流后释放真实麦克风会话");
                        }
                    }
                    var status = await Request("session.status");
                    Check(status.GetProperty("cameraJobs").GetInt32() == 0 && status.GetProperty("microphoneJobs").GetInt32() == 0,
                        "宿主内设备流程结束后助手没有残留设备会话");
                    if (Environment.GetEnvironmentVariable("SystemTools_M5_ManualAuthentication") == "1")
                    {
                        var authentication = provider.GetRequiredService<PlatformMediaServices>().Authentication;
                        Check(authentication.Probe().CanExecute, "Mac 本人系统认证后端实际可用");
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
                        if (Environment.GetEnvironmentVariable("SystemTools_M5_AuthenticationCancelOnly") != "1")
                        {
                            var verified = await authentication.VerifyAsync("SystemTools M5 验收第一步：请完成本人系统认证", 0, deadline.Token);
                            Check(verified.Verified, "未改动宿主内的 Mac 系统认证由本人验证成功");
                        }
                        var rejected = await authentication.VerifyAsync("SystemTools M5 验收第二步：请点击取消，验证不会放行", 0, deadline.Token);
                        Check(!rejected.Verified, "本人取消系统认证后不放行");
                        using var cancelled = new CancellationTokenSource();
                        cancelled.Cancel();
                        try { await authentication.VerifyAsync("不应显示", 0, cancelled.Token); throw new InvalidOperationException("预取消认证被执行"); }
                        catch (OperationCanceledException) { Check(true, "预取消认证不创建系统认证请求"); }
                    }
                }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "acceptance.json"), JsonSerializer.Serialize(new { passed = failures.Count == 0,
                    checks, failures, identity, physical, physicalDeviceAcceptance = physical.Count == 2 && physical.Values.All(value => value) },
                    new JsonSerializerOptions { WriteIndented = true }));
                await Dispatcher.UIThread.InvokeAsync(() => AppBase.Current.Stop());
            }
        };
        AppBase.Current.AppStarted += started;
    }
}

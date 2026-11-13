using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using SystemTools;
using SystemTools.Common.Storage;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;

var runtime = args.FirstOrDefault() ?? "linux-x64";
var temporary = Path.Combine(Path.GetTempPath(), "systemtools-m3-host-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine($"PASS {++checks}: {message}"); }
try
{
    var configFolder = Path.Combine(temporary, "Config");
    Directory.CreateDirectory(configFolder);
    var configPath = Path.Combine(configFolder, "Main.json");
    File.WriteAllText(configPath, """{"enableWindowsHello":true,"enableFfmpegFeatures":true,"aiApiKey":"dpapi-current-user:v1:REDACTED","floatingWindowLiquidGlass":{"cornerRadius":20,"futurePlatformField":{"keep":7}},"unknownRoot":{"keep":8},"floatingWindowButtonRows":[["unknown-button"]]}""");
    File.WriteAllText(configPath + ".bak", "{\"aiApiKey\":\"legacy-plaintext-fixture\"}");
    var services = new ServiceCollection();
    services.AddLogging();
    var catalog = new FeatureCatalog(runtime);
    services.AddSingleton(catalog);
    SharedHostRegistration.Initialize(configFolder, temporary, services, catalog);
    Check(catalog.CanCreate("SystemTools.Copy") && !catalog.CanCreate("SystemTools.CameraCapture"), "共享注册仅开放已接入实现");
    Check(IAutomationService.RegisteredTriggers.Count == 0 && IRulesetService.Rules.Count == 4, "共享注册不捏造原生触发器并提供四项基础规则");
    Check(ComponentRegistryService.Registered.Count == 6, "六项基础组件完成真实宿主契约注册");
    Check(services.Count(x => x.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)) == 1 && services.Count(x => x.ServiceType == typeof(SystemToolsNotificationProvider)) == 1, "通知提供方与托管服务各注册一次");
    using var provider = services.BuildServiceProvider();
    var main = provider.GetRequiredService<MainConfigHandler>();
    main.Data.FloatingWindowLiquidGlass.CornerRadius = 21;
    main.Save();
    using (var saved = JsonDocument.Parse(File.ReadAllText(configPath)))
    {
        var root = saved.RootElement;
        Check(root.GetProperty("aiApiKey").GetString() == "dpapi-current-user:v1:REDACTED" && main.Data.IsAiApiKeyUnavailable, "共享配置保留不可解密 DPAPI 密文");
        Check(root.GetProperty("enableWindowsHello").GetBoolean() && root.GetProperty("enableFfmpegFeatures").GetBoolean(), "其他平台保存不清空 Windows 开关");
        Check(root.GetProperty("floatingWindowLiquidGlass").GetProperty("futurePlatformField").GetProperty("keep").GetInt32() == 7, "实际配置模型保留嵌套未知字段");
        Check(root.GetProperty("floatingWindowButtonRows")[0][0].GetString() == "unknown-button", "共享初始化保留不可用按钮布局");
    }
    var source = Path.Combine(temporary, "source.txt");
    var copied = Path.Combine(temporary, "copied.txt");
    var moved = Path.Combine(temporary, "moved.txt");
    File.WriteAllText(source, "real-host-contract-file");
    async Task Invoke(string id, object settings)
    {
        var action = provider.GetRequiredKeyedService<ActionBase>(id);
        var item = new ActionItem { Id = id, Settings = settings };
        var set = new ActionSet(); set.ActionItems.Add(item); set.SetStartRunning(true);
        try { await action.InvokeAsync(item, set, isRevertable: false); }
        finally { set.SetEndRunning(true); }
        Check(item.Exception is null && item.IsCompleted, id + " 通过宿主行动生命周期");
    }
    await Invoke("SystemTools.Copy", new CopySettings { SourcePath = source, DestinationPath = copied, OperationType = "文件" });
    await Invoke("SystemTools.Move", new MoveSettings { SourcePath = copied, DestinationPath = moved, OperationType = "文件" });
    await Invoke("SystemTools.Delete", new DeleteSettings { TargetPath = moved, OperationType = "文件" });
    Check(File.Exists(source) && !File.Exists(copied) && !File.Exists(moved), "真实文件内容与行动最终状态一致");
    var store = provider.GetRequiredService<AiConversationStore>();
    var conversation = store.CreateConversation(); conversation.Title = "shared-conversation"; store.Save();
    var reloaded = new AiConversationStore();
    Check(reloaded.Conversations.Single().Id == conversation.Id && reloaded.Conversations.Single().Title == "shared-conversation", "共享 AI 会话实际保存并重新读取");
    var profiles = provider.GetRequiredService<FloatingWindowProfileManager>();
    profiles.MigrateFromLegacyConfig(main.Data);
    Check(Path.GetFullPath(profiles.ProfilesDirectory).StartsWith(configFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "真实方案管理器使用持久配置目录");
    profiles.LoadProfile("Default"); profiles.SaveProfile();
    Check(File.Exists(Path.Combine(profiles.ProfilesDirectory, "Default.json")), "真实方案可读写");
    var shared = typeof(SharedHostRegistration).Assembly;
    using var stream = File.OpenRead(shared.Location); using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    var references = metadata.AssemblyReferences.Select(x => metadata.GetString(metadata.GetAssemblyReference(x).Name)).ToArray();
    Check(!references.Any(x => x.StartsWith("SystemTools.Platforms.") || x is "System.Windows.Forms" or "System.Management" or "System.Speech" or "OpenCvSharp" or "DlibDotNet"), "共享程序集元数据不依赖 Windows 原生业务");
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name is "SystemTools.Platforms.Windows" or "OpenCvSharp" or "DlibDotNet"), "共享业务执行不加载 Windows 后端或采集库");
    Console.WriteLine($"M3 managed host checks: {checks}; configured backend={runtime}; execution OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}; this is not target-platform runtime acceptance.");
}
finally { Directory.Delete(temporary, recursive: true); }

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Enums;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NPOI.HSSF.UserModel;
using NPOI.XSSF.UserModel;
using SystemTools.Contracts;
using SystemTools.ConfigHandlers;
using SystemTools.Models;
using SystemTools.Rules;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.M3.DesktopProbe;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    private readonly List<string> _checks = [];
    private readonly List<string> _failures = [];
    private string _root = "";
    private Window? _window;
    private IServiceProvider _provider = null!;
    private ClassIslandActionAiService? _aiService;
    private readonly List<ComponentPresenter> _presenters = [];

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)!.Resolving += (_, name) =>
            name.Name?.StartsWith("NPOI.", StringComparison.Ordinal) == true
                ? AssemblyLoadContext.GetLoadContext(typeof(SharedHostRegistration).Assembly)!.LoadFromAssemblyName(name)
                : null;
        _root = Environment.GetEnvironmentVariable("SystemTools_M3_ProbeRoot")
            ?? throw new InvalidOperationException("Only run this probe in an explicit isolated sandbox.");
        EventHandler? started = null;
        started = async (_, _) =>
        {
            AppBase.Current.AppStarted -= started;
            await RunAsync();
        };
        AppBase.Current.AppStarted += started;
        AppBase.Current.AppStopping += OnStopping;
    }

    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks.Add(name);
    }

    private async Task RunAsync()
    {
        try
        {
            _provider = IAppHost.Host!.Services;
            var loaded = IPluginService.LoadedPlugins.Single(x => x.Manifest.Id == "SystemTools");
            if (Environment.GetEnvironmentVariable("SystemTools_M3_ProbeCase") == "missing-backend")
            {
                Check(loaded.LoadStatus == PluginLoadStatus.Error && loaded.Exception?.ToString().Contains("SystemTools.Platforms.MacOS") == true,
                    "本机后端缺失由真实宿主明确报告加载失败");
            }
            else
            {
                Check(loaded.LoadStatus == PluginLoadStatus.Loaded, "真实宿主加载正式 SystemTools 身份");
                await RunBusinessAsync();
            }
        }
        catch (Exception error) { _failures.Add(error.ToString()); }
        finally
        {
            Save("acceptance.json");
            var seconds = int.TryParse(Environment.GetEnvironmentVariable("SystemTools_M3_ObserveSeconds"), out var value) ? value : 0;
            if (seconds > 0) await Task.Delay(TimeSpan.FromSeconds(seconds));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _window?.Close();
                AppBase.Current.Stop();
            });
        }
    }

    private async Task RunBusinessAsync()
    {
        var assembly = typeof(global::SystemTools.Plugin).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(assembly)!;
        _aiService = _provider.GetRequiredService<ClassIslandActionAiService>();
        _aiService.StartWarmup();
        var catalog = _provider.GetRequiredService<FeatureCatalog>();
        Check(catalog.RuntimeId == "osx-arm64", "本机选择原生 ARM64 后端");
        var pages = SettingsWindowRegistryService.Registered.Where(x => x.Id is "SystemTools.M1.Diagnostics" or "SystemTools.Migration").ToArray();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "SystemTools M3 · Mac ARM64 隔离验收", FontSize = 22 });
            foreach (var page in pages)
                panel.Children.Add(_provider.GetRequiredKeyedService<SettingsPageBase>(page.Id));
            Check(pages.Length == 2, "诊断与导入设置页在真实 UI 线程构造");
            foreach (var name in new[] { "CardTypeComponent", "ClassWidgets", "NotchStyle" })
            {
                try { Check(AvaloniaXamlLoader.Load(new Uri("avares://SystemTools/Themes/" + name + "/Styles.axaml")) is not null, name + " 旧主题资源可加载"); }
                catch (Exception error) { _failures.Add(name + ": " + error); }
            }
            var components = ComponentRegistryService.Registered.Where(x => x.ComponentType?.Assembly == typeof(SharedHostRegistration).Assembly).ToArray();
            foreach (var info in components)
            {
                var presenter = new ComponentPresenter { Settings = new ComponentSettings { Id = info.Guid.ToString() } };
                _presenters.Add(presenter);
                panel.Children.Add(presenter);
                Check(presenter.PresentingContent is ComponentBase, info.ComponentType!.Name + " 经宿主组件服务挂载成功");
            }
            Check(components.Length == 6, "六项共享组件保留正式 GUID");
            _window = new Window { Title = "SystemTools M3 Mac 验收", Width = 860, Height = 760, Content = new ScrollViewer { Content = panel } };
            _window.Show();
        });
        await Task.Delay(800);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var presenter in _presenters)
                Check(presenter.PresentingContent is ComponentBase { IsLoaded: true }, presenter.Settings!.Id + " 已加载到真实视觉树");
        });
        Check(new[] { "上课.png", "无课程.png", "课间休息.png" }.All(name => AssetLoader.Exists(new Uri("avares://SystemTools/Themes/ClassWidgets/" + name))), "旧图像 URI 可解析");
        var actions = IActionService.ActionInfos.Keys.Where(x => x.StartsWith("SystemTools.")).ToArray();
        foreach (var id in actions) Check(_provider.GetRequiredKeyedService<ActionBase>(id) is not null, id + " 行动工厂可解析");
        Check(IRulesetService.Rules.Keys.Count(x => x.StartsWith("SystemTools.")) == 4 &&
              !IAutomationService.RegisteredTriggers.Any(x => x.Id.StartsWith("SystemTools.")), "共享规则齐全且未捏造原生触发器");
        foreach (var rule in IRulesetService.Rules.Where(x => x.Key.StartsWith("SystemTools.")))
            Check(rule.Value.Handle is not null && rule.Value.SettingsType is not null, rule.Key + " 处理器及设置契约已接入");
        Check(!catalog.Get("SystemTools.CameraCapture").CanExecute && !catalog.Get("SystemTools.SetVolume").CanExecute, "未实现的原生能力没有开放");
        Check(SharedRuleHandlers.HandleProcessRunningRule(new ProcessRunningRuleSettings { ProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName }), "进程规则识别真实宿主进程");
        Check(SharedRuleHandlers.HandleInTimePeriodRule(new InTimePeriodRuleSettings { StartTime = "00:00:00", EndTime = "23:59:59" }), "时间规则读取真实宿主时间");
        Check(!SharedRuleHandlers.HandleUsingClassPlanRule(new UsingClassPlanRuleSettings { ClassPlanId = Guid.NewGuid().ToString() }) &&
              !SharedRuleHandlers.HandleUsingTimeLayoutRule(new UsingTimeLayoutRuleSettings { TimeLayoutId = Guid.NewGuid().ToString() }), "档案与时间表规则拒绝不存在的身份");
        await CheckFileActionsAsync();
        CheckExcel();
        var conversations = _provider.GetRequiredService<AiConversationStore>();
        var conversation = conversations.CreateConversation();
        conversation.Title = "M3 macOS synthetic fixture";
        conversations.Save();
        Check(new AiConversationStore().Conversations.Any(x => x.Id == conversation.Id && x.Title == conversation.Title), "AI 会话写入隔离目录并重新读取");
        var profiles = _provider.GetRequiredService<FloatingWindowProfileManager>();
        profiles.LoadProfile("Default"); profiles.SaveProfile();
        Check(Path.GetFullPath(profiles.ProfilesDirectory).StartsWith(Path.GetFullPath(GlobalConstants.PluginConfigFolder!) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "悬浮窗方案位于持久配置目录");
        await CheckMigrationAsync();
        var main = GlobalConstants.MainConfig!;
        main.Save();
        using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(GlobalConstants.PluginConfigFolder!, "Main.json"))))
        {
            Check(saved.RootElement.GetProperty("aiApiKey").GetString() == "dpapi-current-user:v1:REDACTED-INACCESSIBLE" && main.Data.IsAiApiKeyUnavailable, "Mac 保留不可解密的 Windows 密文");
            Check(saved.RootElement.GetProperty("futurePlatform").GetProperty("keep").GetInt32() == 7 && saved.RootElement.GetProperty("enableWindowsHello").GetBoolean(), "配置保存保留未知字段和 Windows 开关");
        }
        Check(context.Assemblies.Count(x => x.GetName().Name!.StartsWith("SystemTools.Platforms.")) == 1, "只加载一个本机后端");
        Check(!context.Assemblies.Any(x => x.GetName().Name is "SystemTools.Platforms.Windows" or "SystemTools.Platforms.Linux" or "System.Windows.Forms" or "System.Speech" or "OpenCvSharp" or "DlibDotNet"), "Windows 和异平台依赖没有泄漏");
        Check(!context.Assemblies.Any(x => x.GetName().Name!.StartsWith("ClassIsland.") || x.GetName().Name!.StartsWith("Avalonia")), "宿主程序集由宿主加载上下文共享");
        Check(context.Assemblies.Any(x => x.GetName().Name == "SuperAutoIsland.Interface"), "M3 允许已共享的 SAI 接口且未要求安装扩展");
        await _provider.GetRequiredService<DiagnosticService>().RunHostSmokeAsync();
        using (var smoke = JsonDocument.Parse(File.ReadAllText(Path.Combine(GlobalConstants.PluginConfigFolder!, "M1.HostSmoke.json"))))
            Check(smoke.RootElement.GetProperty("passed").GetBoolean(), "原诊断行动入口符合 M3 共享依赖契约");
        File.WriteAllText(Path.Combine(_root, "assemblies.json"), JsonSerializer.Serialize(context.Assemblies.Select(x => new { name = x.GetName().Name, path = x.Location }), Options));
    }

    private async Task CheckFileActionsAsync()
    {
        var root = Path.Combine(_root, "file-fixtures"); Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.txt"); var copied = Path.Combine(root, "copied.txt"); var moved = Path.Combine(root, "moved.txt");
        File.WriteAllText(source, "M3 macOS file fixture");
        async Task Invoke(string id, object settings)
        {
            var item = new ActionItem { Id = id, Settings = settings };
            var set = new ActionSet(); set.ActionItems.Add(item); set.SetStartRunning(true);
            try { await _provider.GetRequiredKeyedService<ActionBase>(id).InvokeAsync(item, set, isRevertable: false); }
            finally { set.SetEndRunning(true); }
            Check(item.Exception is null && item.IsCompleted, id + " 完整行动生命周期成功");
        }
        await Invoke("SystemTools.Copy", new CopySettings { SourcePath = source, DestinationPath = copied, OperationType = "文件" });
        Check(File.ReadAllText(copied) == File.ReadAllText(source), "复制内容完整");
        await Invoke("SystemTools.Move", new MoveSettings { SourcePath = copied, DestinationPath = moved, OperationType = "文件" });
        await Invoke("SystemTools.Delete", new DeleteSettings { TargetPath = moved, OperationType = "文件" });
        Check(File.Exists(source) && !File.Exists(copied) && !File.Exists(moved), "文件链最终状态正确");
    }

    private void CheckExcel()
    {
        var profile = new ClassIslandProfileDocument { Name = "M3 Mac fixture" };
        profile.Subjects["sample"] = new ClassIslandSubject { Name = "数学", TeacherName = "测试教师" };
        var source = new ProfileExportSource { FilePath = "synthetic.json", Profile = profile };
        foreach (var xlsx in new[] { true, false })
        {
            var bytes = _provider.GetRequiredService<ClassIslandProfileExcelExporter>().BuildWorkbook(source, new ProfileExcelExportOptions { ExportSubject = true, UseXlsx = xlsx });
            File.WriteAllBytes(Path.Combine(_root, xlsx ? "subjects.xlsx" : "subjects.xls"), bytes);
            using var input = new MemoryStream(bytes);
            using NPOI.SS.UserModel.IWorkbook workbook = xlsx ? new XSSFWorkbook(input) : new HSSFWorkbook(input);
            Check(workbook.NumberOfSheets == 1 && workbook.GetSheetAt(0).LastRowNum >= 1, (xlsx ? "XLSX" : "XLS") + " 实际生成并重读");
        }
    }

    private async Task CheckMigrationAsync()
    {
        var root = GlobalConstants.PluginConfigFolder!;
        var legacy = Path.Combine(root, "import-fixture"); Directory.CreateDirectory(legacy);
        var source = Path.Combine(legacy, "imported.json"); var target = Path.Combine(root, "imported.json");
        File.WriteAllText(source, "{\"enabledActions\":{\"SystemTools.CrossPlatform.Copy\":true},\"text\":\"SystemTools.CrossPlatform.Copy\"}");
        const string original = "{\"keepBeforeMigration\":true}"; File.WriteAllText(target, original);
        TextBlock output = null!;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var page = _provider.GetRequiredKeyedService<SettingsPageBase>("SystemTools.Migration");
            var panel = (StackPanel)((ScrollViewer)page.Content!).Content!;
            panel.Children.OfType<TextBox>().Single().Text = legacy;
            var buttons = panel.Children.OfType<Button>().ToArray();
            buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var row = panel.Children.OfType<StackPanel>().Single().Children.OfType<StackPanel>().Single();
            var choice = row.Children.OfType<ComboBox>().Single();
            Check(choice.SelectedIndex == 0 && buttons[1].IsEnabled, "真实导入页冲突默认保留现有数据");
            choice.SelectedIndex = 1; output = panel.Children.OfType<TextBlock>().Last();
            buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        for (var i = 0; i < 100; i++)
        {
            if (await Dispatcher.UIThread.InvokeAsync(() => output.Text?.StartsWith("导入完成", StringComparison.Ordinal) == true)) break;
            if (i == 99) throw new TimeoutException("导入按钮没有完成迁移：" + output.Text);
            await Task.Delay(50);
        }
        using (var saved = JsonDocument.Parse(File.ReadAllText(target)))
            Check(saved.RootElement.GetProperty("enabledActions").GetProperty("SystemTools.Copy").GetBoolean() && saved.RootElement.GetProperty("text").GetString() == "SystemTools.CrossPlatform.Copy", "真实导入按钮转换身份且保留普通文本");
        var journal = Directory.EnumerateFiles(Path.Combine(root, ".migrations"), "journal.json", SearchOption.AllDirectories).Single();
        SystemTools.Common.Migration.IdentityMigration.Rollback(journal);
        Check(File.ReadAllText(target) == original && File.Exists(source), "真实迁移回退保留来源并恢复原文");
    }

    private void OnStopping(object? sender, EventArgs args)
    {
        AppBase.Current.AppStopping -= OnStopping;
        try
        {
            if (Environment.GetEnvironmentVariable("SystemTools_M3_ProbeCase") != "missing-backend")
            {
                var entry = _provider.GetRequiredService<global::SystemTools.Plugin>();
                var scope = typeof(global::SystemTools.Plugin).GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!;
                Check(scope.GetType().GetField("_cleanup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope) is null, "插件停止关闭资源域");
                var started = AppBase.Current.GetType().GetField("AppStarted", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(AppBase.Current) as Delegate;
                Check(started?.GetInvocationList().All(x => x.Method.DeclaringType?.Assembly != typeof(global::SystemTools.Plugin).Assembly) != false, "插件启动事件已退订");
                if (_aiService is not null)
                {
                    var cancellation = (CancellationTokenSource)typeof(ClassIslandActionAiService).GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_aiService)!;
                    Check(cancellation.IsCancellationRequested, "共享 AI 后台任务在正常退出时取消");
                }
            }
        }
        catch (Exception error) { _failures.Add(error.ToString()); }
        Save("shutdown.json");
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private void Save(string name) => File.WriteAllText(Path.Combine(_root, name), JsonSerializer.Serialize(new
    {
        passed = _failures.Count == 0, checks = _checks, failures = _failures,
        executionOS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
    }, Options));
}

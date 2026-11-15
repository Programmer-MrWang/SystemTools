using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemTools.Common.Automation;
using SystemTools.Contracts;
using SystemTools.Controls;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools;

public static class PortableExtendedActions
{
    public static void Register(IServiceCollection services, FeatureCatalog catalog, NativeDesktopServices native)
    {
        var names = new List<string>();
        void Bind(string suffix, Func<Capability> probe)
        {
            var id = "SystemTools." + suffix; names.Add(id);
            catalog.BindForRegistration(id, () => !GlobalConstants.MainConfig!.Data.IsActionEnabled(id)
                ? new(CapabilityState.Disabled, "用户已关闭。") : probe());
        }
        if (native.Keyboard is { } keyboard)
        {
            foreach (var suffix in new[] { "AltF4", "AltTab", "CtrlZ", "TypeContent" }) Bind(suffix, keyboard.Probe);
            if (catalog.CanCreate("SystemTools.AltF4")) services.AddAction<PortableCloseKeyAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.AltTab")) services.AddAction<PortableSwitchAppAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.CtrlZ")) services.AddAction<PortableUndoAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.TypeContent")) services.AddAction<PortableTypeAction, TypeContentSettingsControl>();
        }
        if (native.Pointer is { } pointer)
        {
            services.AddSingleton(pointer); Bind("SimulateMouse", pointer.Probe);
            if (catalog.CanCreate("SystemTools.SimulateMouse")) services.AddAction<PortableMouseAction, PortableMouseSettingsControl>();
        }
        Bind("KillProcess", () => new(CapabilityState.Available));
        if (catalog.CanCreate("SystemTools.KillProcess")) services.AddAction<PortableKillAction, KillProcessSettingsControl>();
        if (native.Session is { } session)
        {
            services.AddSingleton(session);
            services.AddSingleton<PortableShutdownService>();
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PortableShutdownService>());
            foreach (var (suffix, operation) in new[] { ("ImmediateShutdown","shutdown"),("ImmediateRestart","restart"),("Sleep","sleep"),("LockScreen","lock"),("ShowDesktop","desktop"),
                ("Shutdown","shutdown"),("AdvancedShutdown","shutdown"),("CancelShutdown","cancel"),("SwitchTheme","theme.set"),("SwitchSystemAccentColor","accent.set") })
                Bind(suffix, () => operation == "cancel" ? new(CapabilityState.Available) : session.Probe(operation));
            if (catalog.CanCreate("SystemTools.ImmediateShutdown")) services.AddAction<PortableShutdownNowAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.ImmediateRestart")) services.AddAction<PortableRestartAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.Sleep")) services.AddAction<PortableSleepAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.LockScreen")) services.AddAction<PortableLockAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.ShowDesktop")) services.AddAction<PortableDesktopAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.Shutdown")) services.AddAction<PortableShutdownAction, ShutdownSettingsControl>();
            if (catalog.CanCreate("SystemTools.AdvancedShutdown")) services.AddAction<PortableAdvancedShutdownAction, AdvancedShutdownSettingsControl>();
            if (catalog.CanCreate("SystemTools.CancelShutdown")) services.AddAction<PortableCancelShutdownAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.SwitchTheme")) services.AddAction<PortableThemeAction, ThemeSettingsControl>();
            if (catalog.CanCreate("SystemTools.SwitchSystemAccentColor")) services.AddAction<PortableAccentAction, PortableAccentSettingsControl>();
        }
        var group = new ActionMenuTreeGroup("更多系统行动", "\uE079");
        foreach (var id in names)
            if (catalog.CanCreate(id)) group.Children.Add(new ActionMenuTreeItem(id, catalog.Snapshot().Single(x => x.Feature.Id == id).Feature.Name, "\uE079"));
        if (group.Children.Count > 0) ClassIsland.Core.Abstractions.Services.IActionService.ActionMenuTree.Add(group);
    }
}

public abstract class PortableShortcut<TSettings>(IKeyboardInput input, NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<TSettings> where TSettings : ShortcutKeyNotificationSettings
{
    protected abstract string[] Keys { get; }
    protected string RuntimeId => catalog.RuntimeId;
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        await PortableKeyboard.ExecuteAsync(input, Keys.Select(x => (x, 1, 0L)).ToArray(), "", stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, "快捷键已发送");
    }
}
[ActionInfo("SystemTools.AltF4","关闭当前窗口","\uEA0F",false)]
public sealed class PortableCloseKeyAction(IKeyboardInput input, NativeDesktopServices native, FeatureCatalog catalog) : PortableShortcut<ShortcutKeyNotificationSettings>(input,native,catalog)
{ protected override string[] Keys => RuntimeId == "osx-arm64" ? ["Meta","W"] : ["Alt","F4"]; }
[ActionInfo("SystemTools.AltTab","切换应用","\uEA0F",false)]
public sealed class PortableSwitchAppAction(IKeyboardInput input, NativeDesktopServices native, FeatureCatalog catalog) : PortableShortcut<ShortcutKeyNotificationSettings>(input,native,catalog)
{ protected override string[] Keys => RuntimeId == "osx-arm64" ? ["Meta","Tab"] : ["Alt","Tab"]; }
[ActionInfo("SystemTools.CtrlZ","撤销","\uEA0F",false)]
public sealed class PortableUndoAction(IKeyboardInput input, NativeDesktopServices native, FeatureCatalog catalog) : PortableShortcut<ShortcutKeyNotificationSettings>(input,native,catalog)
{ protected override string[] Keys => RuntimeId == "osx-arm64" ? ["Meta","Z"] : ["Control","Z"]; }
[ActionInfo("SystemTools.TypeContent","键入内容","\uE4BE",false)]
public sealed class PortableTypeAction(IKeyboardInput input, NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<TypeContentSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken,native.Stopping);
        await PortableKeyboard.ExecuteAsync(input, [], Settings.Content, stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute,"内容已键入");
    }
}
[ActionInfo("SystemTools.SimulateMouse","模拟鼠标","\uE5C1",false)]
public sealed class PortableMouseAction(IPointerInput pointer, NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<MouseInputSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        if(Settings.DisableMouseDuringExecution) throw new PlatformNotSupportedException("本平台不支持禁用鼠标硬件，请关闭该选项后执行。");
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken,native.Stopping);
        await PortablePointer.ExecuteAsync(pointer,Settings.Actions.Select(x=>new PointerStep((int)x.Type,x.X,x.Y,x.ScrollDelta,x.Interval,x.IsDragEnd)).ToArray(),stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute,"鼠标输入已完成");
    }
}
[ActionInfo("SystemTools.KillProcess","退出进程","\uE0DE",false)]
public sealed class PortableKillAction : ActionBase<KillProcessSettings>
{
    protected override async Task OnInvoke()
    {
        var name=Settings.ProcessName.Trim();
        if(name.Length==0 || name.Contains('/') || name.Contains('\\')) throw new ArgumentException("请输入精确进程名。");
        var processes=Process.GetProcessesByName(name);
        if(processes.Any(x=>x.Id==Environment.ProcessId)) { foreach(var process in processes)process.Dispose(); throw new InvalidOperationException("请使用宿主退出入口，不能终止插件自身宿主。"); }
        List<Exception> errors=[];
        foreach(var process in processes)
            using(process)
            {
                InterruptCancellationToken.ThrowIfCancellationRequested();
                try { process.Kill(); await process.WaitForExitAsync(InterruptCancellationToken).WaitAsync(TimeSpan.FromSeconds(5)); }
                catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException) { errors.Add(error); }
            }
        if(errors.Count>0)throw new AggregateException("部分进程无法退出，请检查目标和权限。",errors);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute,"指定进程已退出");
    }
}
public abstract class PortableSessionAction<TSettings>(IDesktopSession session, NativeDesktopServices native, FeatureCatalog catalog,string operation) : ActionBase<TSettings> where TSettings : ShortcutKeyNotificationSettings
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken,native.Stopping);
        await session.ExecuteAsync(operation,null,stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute,"已向系统提交操作");
    }
}
[ActionInfo("SystemTools.ImmediateShutdown","立即关机","\uE4C4",false)]
public sealed class PortableShutdownNowAction(IDesktopSession session,NativeDesktopServices native,FeatureCatalog catalog):PortableSessionAction<ShortcutKeyNotificationSettings>(session,native,catalog,"shutdown");
[ActionInfo("SystemTools.ImmediateRestart","立即重启","\uE4C4",false)]
public sealed class PortableRestartAction(IDesktopSession session,NativeDesktopServices native,FeatureCatalog catalog):PortableSessionAction<ShortcutKeyNotificationSettings>(session,native,catalog,"restart");
[ActionInfo("SystemTools.Sleep","睡眠","\uE4C4",false)]
public sealed class PortableSleepAction(IDesktopSession session,NativeDesktopServices native,FeatureCatalog catalog):PortableSessionAction<ShortcutKeyNotificationSettings>(session,native,catalog,"sleep");
[ActionInfo("SystemTools.LockScreen","锁定屏幕","\uE4C4",false)]
public sealed class PortableLockAction(IDesktopSession session,NativeDesktopServices native,FeatureCatalog catalog):PortableSessionAction<ShortcutKeyNotificationSettings>(session,native,catalog,"lock");
[ActionInfo("SystemTools.ShowDesktop","显示桌面","\uE079",false)]
public sealed class PortableDesktopAction(IDesktopSession session,NativeDesktopServices native,FeatureCatalog catalog):PortableSessionAction<ShortcutKeyNotificationSettings>(session,native,catalog,"desktop");

public sealed class PortableShutdownService(IDesktopSession session,NativeDesktopServices native,ILogger<PortableShutdownService> logger):IHostedService,IDisposable
{
    public ScheduledOperation Schedule {get;}=new();
    public void Start(int seconds)
    {
        Schedule.Schedule(TimeSpan.FromSeconds(seconds),async token=> { await session.ExecuteAsync("shutdown",null,token); },native.Stopping);
        _=Schedule.Completion.ContinueWith(_=> { if(Schedule.LastError is {} error)logger.LogError(error,"计时关机被系统拒绝。"); },TaskScheduler.Default);
    }
    public Task StartAsync(CancellationToken token)=>Task.CompletedTask;
    public Task StopAsync(CancellationToken token) { Schedule.Cancel(); return Schedule.Completion; }
    public void Dispose()=>Schedule.Dispose();
}
[ActionInfo("SystemTools.Shutdown","计时关机","\uE4C4",false)]
public sealed class PortableShutdownAction(PortableShutdownService service):ActionBase<ShutdownSettings>
{
    protected override Task OnInvoke() { InterruptCancellationToken.ThrowIfCancellationRequested(); service.Start(Settings.Seconds); NativeActionFeedback.Notify(Settings.NotifyOnExecute,"已建立插件计时关机计划；退出宿主会取消");return Task.CompletedTask; }
    protected override Task OnInterrupted(){service.Schedule.Cancel();return Task.CompletedTask;}
    protected override Task OnRevert()=>OnInterrupted();
}
[ActionInfo("SystemTools.CancelShutdown","取消关机计划","\uE4C4",false)]
public sealed class PortableCancelShutdownAction(PortableShutdownService service):ActionBase<ShortcutKeyNotificationSettings>
{ protected override Task OnInvoke(){service.Schedule.Cancel();NativeActionFeedback.Notify(Settings.NotifyOnExecute,"已取消本插件的关机计划");return Task.CompletedTask;} }
[ActionInfo("SystemTools.AdvancedShutdown","高级计时关机","\uE4C4",false)]
public sealed class PortableAdvancedShutdownAction(PortableShutdownService service,PortableSurfaceLifetime lifetime):ActionBase<AdvancedShutdownSettings>
{
    private Window? _window;
    protected override async Task OnInvoke()
    {
        InterruptCancellationToken.ThrowIfCancellationRequested();service.Start(checked(Settings.Minutes*60));
        await Dispatcher.UIThread.InvokeAsync(()=>
        {
            _window?.Close();
            var remaining=new TextBlock();var cancel=new Button{Content="取消关机计划"};var extend=new Button{Content="延长 1 分钟"};
            var status=new TextBlock{TextWrapping=TextWrapping.Wrap};
            var window=new Window{Title="SystemTools 计时关机",Width=430,Height=250,Content=new StackPanel{Margin=new Avalonia.Thickness(24),Spacing=12,Children={remaining,extend,cancel,status}}};
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
            void Refresh()
            {
                remaining.Text=service.Schedule.Due is {} due ? "距离关机："+Math.Max(0,(int)Math.Ceiling((due-DateTimeOffset.UtcNow).TotalSeconds))+" 秒" : "计划已结束";
                if(service.Schedule.LastError is not null)status.Text="系统拒绝了关机请求，请检查自动化或系统权限。";
            }
            timer.Tick+=(_,_)=>Refresh();window.Closed+=(_,_)=>timer.Stop();
            cancel.Click+=(_,_)=>{service.Schedule.Cancel();window.Close();};
            extend.Click+=(_,_)=>{if(service.Schedule.Due is {} due)service.Start((int)Math.Clamp(Math.Ceiling((due-DateTimeOffset.UtcNow).TotalSeconds)+60,0,604800));};
            Refresh();timer.Start();lifetime.Track(window);_window=window;window.Show();
        });
    }
    protected override async Task OnInterrupted(){service.Schedule.Cancel();await Dispatcher.UIThread.InvokeAsync(()=>_window?.Close());}
    protected override Task OnRevert()=>OnInterrupted();
}
[ActionInfo("SystemTools.SwitchTheme","切换系统主题","\uF42F",false)]
public sealed class PortableThemeAction(IDesktopSession session,FeatureCatalog catalog):ActionBase<ThemeSettings>
{
    private string? _before,_applied;
    protected override async Task OnInvoke()
    {
        _before=await session.ExecuteAsync("theme.read",null,InterruptCancellationToken);
        _applied=catalog.RuntimeId=="osx-arm64" ? (Settings.Theme=="深色" ? "dark":"light") : (Settings.Theme=="深色" ? "prefer-dark":"default");
        await session.ExecuteAsync("theme.set",_applied,InterruptCancellationToken);
        if(catalog.RuntimeId=="osx-arm64" && await session.ExecuteAsync("theme.read",null,InterruptCancellationToken)!=_applied)throw new IOException("系统未确认主题变更。");
        NativeActionFeedback.Notify(Settings.NotifyOnExecute,"系统主题已切换");
    }
    protected override async Task OnRevert(){if(_before is not null && await session.ExecuteAsync("theme.read",null,default)==_applied)await session.ExecuteAsync("theme.set",_before,default);}
}
[ActionInfo("SystemTools.SwitchSystemAccentColor","切换系统强调色","\uE523",false)]
public sealed class PortableAccentAction(IDesktopSession session):ActionBase<AccentColorSettings>
{
    public static readonly (string Name,string Hex)[] Palette=[("blue","#3584E4"),("teal","#2190A4"),("green","#3A944A"),("yellow","#C88800"),("orange","#ED5B00"),("red","#E62D42"),("pink","#D56199"),("purple","#9141AC"),("slate","#6F8396")];
    private string? _before,_applied;
    protected override async Task OnInvoke()
    {
        var color=Color.Parse(Settings.ColorHex);
        _applied=Palette.OrderBy(x=>{var other=Color.Parse(x.Hex);return Math.Pow(other.R-color.R,2)+Math.Pow(other.G-color.G,2)+Math.Pow(other.B-color.B,2);}).First().Name;
        _before=await session.ExecuteAsync("accent.read",null,InterruptCancellationToken);
        await session.ExecuteAsync("accent.set",_applied,InterruptCancellationToken);
    }
    protected override async Task OnRevert(){if(_before is not null && await session.ExecuteAsync("accent.read",null,default)==_applied)await session.ExecuteAsync("accent.set",_before,default);}
}
public sealed class PortableAccentSettingsControl:ClassIsland.Core.Abstractions.Controls.ActionSettingsControlBase<AccentColorSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();var picker=new ComboBox{ItemsSource=PortableAccentAction.Palette.Select(x=>x.Name).ToArray()};
        picker.SelectionChanged+=(_,_)=>{if(picker.SelectedIndex>=0)Settings.ColorHex=PortableAccentAction.Palette[picker.SelectedIndex].Hex;};
        var selected=Array.FindIndex(PortableAccentAction.Palette,x=>x.Hex.Equals(Settings.ColorHex,StringComparison.OrdinalIgnoreCase));picker.SelectedIndex=selected;
        Content=new StackPanel{Spacing=8,Children={new TextBlock{Text="GNOME 支持固定系统色板；旧自定义颜色会选择最接近的系统色。",TextWrapping=TextWrapping.Wrap},picker}};
    }
}
public sealed class PortableMouseSettingsControl:ClassIsland.Core.Abstractions.Controls.ActionSettingsControlBase<MouseInputSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var list = new StackPanel { Spacing = 12 };
        void Refresh()
        {
            list.Children.Clear();
            foreach (var step in Settings.Actions.Take(500).ToArray())
            {
                var kind = new ComboBox { ItemsSource = new[] { "左键单击", "右键单击", "中键单击", "滚轮", "拖动" }, SelectedIndex = (int)step.Type };
                kind.SelectionChanged += (_, _) => { if (kind.SelectedIndex >= 0) step.Type = (MouseAction.ActionType)kind.SelectedIndex; };
                var card = new StackPanel { Spacing = 6 };
                card.Children.Add(kind);
                void Number(string label, decimal current, decimal minimum, decimal maximum, Action<decimal> write)
                {
                    var field = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = current };
                    field.ValueChanged += (_, _) => { if (field.Value is { } value) write(value); };
                    card.Children.Add(new TextBlock { Text = label }); card.Children.Add(field);
                }
                Number("屏幕 X",step.X,-100000,100000,value=>step.X=(int)value);
                Number("屏幕 Y",step.Y,-100000,100000,value=>step.Y=(int)value);
                Number("滚轮量（120 为一格）",step.ScrollDelta,-12000,12000,value=>step.ScrollDelta=(int)value);
                Number("执行前等待（毫秒）",step.Interval,0,3600000,value=>step.Interval=(long)value);
                var end = new CheckBox { Content = "结束拖动并松开左键", IsChecked = step.IsDragEnd };
                end.IsCheckedChanged += (_, _) => step.IsDragEnd = end.IsChecked == true;
                var remove = new Button { Content = "删除步骤" };
                remove.Click += (_, _) => { Settings.Actions.Remove(step); Refresh(); };
                card.Children.Add(end);card.Children.Add(remove);
                list.Children.Add(new Border { Padding = new Avalonia.Thickness(10), BorderThickness = new Avalonia.Thickness(1), BorderBrush = Brushes.Gray, Child = card });
            }
        }
        var add = new Button { Content = "添加鼠标步骤" };
        add.Click += (_, _) => { if(Settings.Actions.Count < 500) { Settings.Actions.Add(new()); Refresh(); } };
        var disabled = new CheckBox { Content = "禁用物理鼠标（本平台不支持，需关闭后才能执行）", IsChecked = Settings.DisableMouseDuringExecution };
        disabled.IsCheckedChanged += (_, _) => Settings.DisableMouseDuringExecution = disabled.IsChecked == true;
        Refresh();
        Content = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "坐标使用当前平台的屏幕逻辑坐标；换设备或缩放后请重新记录。执行前需激活目标窗口，目标改变时停止。", TextWrapping = TextWrapping.Wrap },
            disabled,new ScrollViewer { MaxHeight = 400, Content = list },add
        }};
    }
}

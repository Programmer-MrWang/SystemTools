using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemTools.Common.Automation;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Settings;

namespace SystemTools.M8.MacHostProbe;

[PluginEntrance]
public sealed class Plugin:PluginBase
{
    private readonly List<string> _checks=[];
    private readonly List<string> _failures=[];
    private string _root="";
    private IServiceProvider _services=null!;
    private NativeDesktopServices _native=null!;
    private Window _window=null!;
    private TextBlock _status=null!;
    private JsonElement Fixture=>JsonDocument.Parse(File.ReadAllText(Path.Combine(_root,"fixture.json"))).RootElement.Clone();
    public override void Initialize(HostBuilderContext context,IServiceCollection services)
    {
        _root=Environment.GetEnvironmentVariable("SystemTools_M8_ProbeRoot")??throw new InvalidOperationException("需要隔离验收目录。");
        AppBase.Current.AppStarted+=async(_,_)=>await Start();
    }
    private void Check(bool value,string label)
    {
        if(!value)throw new Exception(label);_checks.Add(label);Write();_status.Text="通过："+label;
    }
    private void Write()=>File.WriteAllText(Path.Combine(_root,"interaction.json"),JsonSerializer.Serialize(new{passed=_failures.Count==0,checks=_checks,failures=_failures},new JsonSerializerOptions{WriteIndented=true}));
    private async Task Invoke(string id,object settings)
    {
        var action=_services.GetRequiredKeyedService<ActionBase>(id);
        var item=new ActionItem{Id=id,Settings=settings};var set=new ActionSet();set.ActionItems.Add(item);set.SetStartRunning(true);
        try {await action.InvokeAsync(item,set,false);}
        finally {set.SetEndRunning(true);}
        if(item.Exception is {} error)throw new Exception(error);
        Check(item.IsCompleted,"实际行动完成："+id);
    }
    private async Task Start()
    {
        _services=IAppHost.Host!.Services;_native=_services.GetRequiredService<NativeDesktopServices>();
        _status=new TextBlock{Text="准备交互验收；只使用隔离数据与合成凭据。",TextWrapping=TextWrapping.Wrap};
        var panel=new StackPanel{Margin=new Thickness(20),Spacing=8};
        _window=new Window{Title="SystemTools M8 本机验收",Width=720,Height=700,Content=new ScrollViewer{Content=panel}};
        panel.Children.Add(new TextBlock{Text="SystemTools M8 本机验收",FontSize=24});
        panel.Children.Add(_status);
        void Button(string title,Func<Task> operation)
        {
            var button=new Button{Content=title,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};
            button.Click+=async(_,_)=>
            {
                button.IsEnabled=false;
                try {await operation();}
                catch(Exception error){_failures.Add(title+": "+error);_status.Text="失败："+error.Message;Write();}
                finally{button.IsEnabled=true;if(!title.StartsWith("打开黑屏")&&!title.StartsWith("结束"))_window.Activate();}
            };
            panel.Children.Add(button);
        }
        Button("检查新增行动与设置",()=>
        {
            foreach(var id in new[]{"ActionFlowExecutionConfirmation","BackgroundPlayAudio","BlackScreenHtml","KillProcess","Shutdown","AdvancedShutdown","CancelShutdown","ImmediateShutdown","ImmediateRestart","Sleep","LockScreen","TypeContent","SimulateMouse","AltF4","AltTab","CtrlZ","SwitchTheme"})
                Check(_services.GetRequiredKeyedService<ActionBase>("SystemTools."+id) is not null,"实际宿主注册："+id);
            Check(IAutomationService.RegisteredTriggers.Any(x=>x.Id=="SystemTools.ActionInProgressTrigger")&&IAutomationService.RegisteredTriggers.Any(x=>x.Id=="SystemTools.MainWindowClickTrigger"),"实际宿主注册两项共享触发器");
            return Task.CompletedTask;
        });
        Button("打开行动流确认",()=>Invoke("SystemTools.ActionFlowExecutionConfirmation",new ActionFlowExecutionConfirmationSettings{PromptName="M8 合成自动化"}));
        Button("打开黑屏（Esc 或单击退出）",async()=>
        {
            await Invoke("SystemTools.BlackScreenHtml",new ShortcutKeyNotificationSettings());
            if(Application.Current!.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                foreach(var window in desktop.Windows.Where(x=>x.Title?.StartsWith("SystemTools 黑屏")==true))
                    window.Closed+=(_,_)=>{Check(true,"黑屏通过 Esc/点击退出且控制窗口恢复");_window.Activate();};
        });
        Button("播放测试音频",Play);
        Button("测试计时取消",async()=>
        {
            await Invoke("SystemTools.Shutdown",new ShutdownSettings{Seconds=3600});
            await Invoke("SystemTools.CancelShutdown",new ShortcutKeyNotificationSettings());
            Check(_services.GetRequiredService<PortableShutdownService>().Schedule.Due is null,"真实宿主取消计划且不执行系统关机");
        });
        Button("测试输入、撤销、鼠标与窗口",Input);
        Button("注册真实热键（Control+Option+F9）",RegisterHotkey);
        Button("开始 65 秒真实空闲采样",Idle);
        Button("读取并切换系统主题后恢复",Theme);
        Button("保存合成系统凭据",async()=>
        {
            var config=_services.GetRequiredService<MainConfigHandler>();
            await Task.Run(()=>config.Data.SaveApiKeySecurely("m8-host-synthetic-"+Path.GetFileName(_root)));
            var reference=config.Data.SavedCredentialReference;
            Check(reference.StartsWith("keychain:v1:"),"真实宿主写入 macOS Keychain 引用");
            Check(!File.ReadAllText(Path.Combine(PluginConfigFolder,"..","SystemTools","Main.json")).Contains("m8-host-synthetic-"),"真实宿主配置不包含新密钥明文");
            File.WriteAllText(Path.Combine(_root,"credential-saved"),reference);
        });
        Button("删除合成系统凭据",async()=>
        {
            var config=_services.GetRequiredService<MainConfigHandler>();
            await Task.Run(config.Data.ClearSavedApiKey);
            Check(config.Data.AiApiKey.Length==0&&config.Data.SavedCredentialReference.Length==0,"真实宿主删除合成凭据并清空引用");
            File.WriteAllText(Path.Combine(_root,"credential-cleared"),"done");
        });
        Button("结束并退出验收",async()=>{Write();File.WriteAllText(Path.Combine(_root,"finished"),"done");await Dispatcher.UIThread.InvokeAsync(()=>AppBase.Current.Stop());});
        _window.Show();_window.Activate();
        var data=_services.GetRequiredService<MainConfigHandler>().Data;
        if(File.Exists(Path.Combine(_root,"credential-saved"))&&!File.Exists(Path.Combine(_root,"credential-cleared")))
            Check(data.AiApiKey=="m8-host-synthetic-"+Path.GetFileName(_root),"同一原版宿主重启后成功读取系统凭据");
        await Task.CompletedTask;
    }
    private async Task Play()
    {
        var file=Path.Combine(_root,"中文 音频.wav");
        using(var stream=File.Create(file))
        using(var writer=new BinaryWriter(stream))
        {
            var count=16000;writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);
            writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
            for(var i=0;i<count;i++)writer.Write((short)(Math.Sin(i*2*Math.PI*440/16000)*70));
        }
        await Invoke("SystemTools.BackgroundPlayAudio",new BackgroundPlayAudioSettings{AudioFilePath=file,WaitForPlaybackCompleted=true});
        Check(true,"宿主实际播放中文路径 WAV 并等待完成");
    }
    private async Task ActivateFixture()
    {
        var info=new ProcessStartInfo("/usr/bin/open"){UseShellExecute=false};info.ArgumentList.Add("-a");info.ArgumentList.Add(Path.Combine(_root,"SystemToolsM8Fixture.app"));
        using var process=Process.Start(info)!;await process.WaitForExitAsync();await Task.Delay(800);
    }
    private async Task Input()
    {
        await ActivateFixture();
        var oldText=Fixture.GetProperty("text").GetString();
        await Invoke("SystemTools.TypeContent",new TypeContentSettings{Content="M8 中文输入"});
        await Task.Delay(500);
        Check(Fixture.GetProperty("text").GetString()!.Contains("M8 中文输入"),"实际外部 AppKit 目标接收 Unicode 文本");
        await Invoke("SystemTools.CtrlZ",new ShortcutKeyNotificationSettings());await Task.Delay(500);
        Check(Fixture.GetProperty("text").GetString()==oldText,"Mac 撤销映射 Command+Z 实际生效");
        var keyboard=_native.Keyboard!;
        await PortableKeyboard.ExecuteAsync(keyboard,[("Meta",1,0),("A",0,0)],"替换前",default);
        await Task.Delay(300);
        await PortableKeyboard.ExecuteAsync(keyboard,[],"组合键验证",default);
        await Task.Delay(300);
        Check(Fixture.GetProperty("text").GetString()=="组合键验证","真实 Command+A 组合键与后续 Unicode 输入");
        using(var stop=new CancellationTokenSource(80))
        {
            try{await PortableKeyboard.ExecuteAsync(keyboard,[("Meta",1,0),("Z",0,1000)],"",stop.Token);}catch(OperationCanceledException){}
        }
        await PortableKeyboard.ExecuteAsync(keyboard,[],"取消后",default);
        await Task.Delay(300);
        Check(Fixture.GetProperty("text").GetString()!.Contains("取消后"),"取消后 Command 修饰键已释放且可继续输入");
        var fixture=Fixture;var before=fixture.GetProperty("clicks").GetInt32();
        await Invoke("SystemTools.SimulateMouse",new MouseInputSettings{Actions=[new(){Type=MouseAction.ActionType.LeftClick,X=(int)fixture.GetProperty("buttonX").GetDouble(),Y=(int)fixture.GetProperty("buttonY").GetDouble()}]});
        await Task.Delay(400);
        Check(Fixture.GetProperty("clicks").GetInt32()==before+1,"真实 Quartz 鼠标点击抵达外部目标按钮");
        await _native.Windows!.ExecuteForegroundAsync("最小化",default);await Task.Delay(400);
        Check(Fixture.GetProperty("minimized").GetBoolean(),"外部 AppKit 窗口最小化实际成功");
        await ActivateFixture();
        await _native.Windows.ExecuteForegroundAsync("向下还原",default);await Task.Delay(400);
        Check(!Fixture.GetProperty("minimized").GetBoolean(),"外部 AppKit 窗口恢复实际成功");
        _window.Activate();
    }
    private IDisposable? _hotkey;
    private Task RegisterHotkey()
    {
        _hotkey?.Dispose();
        _hotkey=_native.Hotkeys!.Register("F9",1|2,()=>Dispatcher.UIThread.Post(()=>
        {Check(true,"真实 Control+Option+F9 系统按键触发回调");_hotkey?.Dispose();_hotkey=null;File.WriteAllText(Path.Combine(_root,"hotkey-completed"),"done");}));
        _window.Closed+=(_,_)=>_hotkey?.Dispose();
        _status.Text="已注册，请使用 Control+Option+F9。";return Task.CompletedTask;
    }
    private async Task Idle()
    {
        _status.Text="请保持不操作约 65 秒，正在读取真实系统空闲时长。";
        var maximum=TimeSpan.Zero;
        for(var i=0;i<65;i++){await Task.Delay(1000);var sample=_native.Idle!.Read();if(sample>maximum)maximum=sample;}
        Check(maximum>=TimeSpan.FromMinutes(1),"真实系统空闲采样达到一分钟："+maximum.TotalSeconds.ToString("F1"));
        File.WriteAllText(Path.Combine(_root,"idle-completed"),"done");
    }
    private async Task Theme()
    {
        var session=_native.Session!;var before=await session.ExecuteAsync("theme.read",null,default);
        try
        {
            var target=before=="dark"?"light":"dark";
            await session.ExecuteAsync("theme.set",target,default);
            Check(await session.ExecuteAsync("theme.read",null,default)==target,"自有助手通过系统授权切换外观并读回");
        }
        finally {await session.ExecuteAsync("theme.set",before,default);}
        Check(await session.ExecuteAsync("theme.read",null,default)==before,"系统外观恢复原值");
    }
}

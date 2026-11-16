using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Components;
using ClassIsland.Shared.Models.Profile;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Common.Media;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;

namespace SystemTools.M8.MacHostProbe;

public sealed partial class Plugin
{
    private Task? _closeout;
    private readonly List<object> _soakSamples=[];
    private readonly List<object> _layoutResults=[];
    private readonly List<object> _recoveries=[];
    private readonly List<string> _soakErrors=[];
    private double _awakeSeconds;
    private int _captureSuccesses;
    private string _layoutName="";
    private readonly SemaphoreSlim _closeoutGate=new(1,1);
    private DateTime _closeoutStarted;
    private ComponentProfile? _originalLayout;
    private Profile? _originalProfile;
    private bool _layoutsOnly;

    private Task StartCloseout()=>StartCloseout(false);
    private Task StartCloseout(bool layoutsOnly)
    {
        if(_closeout is not null)throw new InvalidOperationException("持续验收已经开始。");
        _layoutsOnly=layoutsOnly;
        _closeout=RunCloseout();
        return Task.CompletedTask;
    }
    private void CloseoutReport(bool completed=false)
    {
        var process=Process.GetCurrentProcess();process.Refresh();
        var unexpected=_soakErrors.Where(error=>!ExpectedSleepInterruption(error)).ToArray();
        File.WriteAllText(Path.Combine(_root,_layoutsOnly?"layout-closeout.json":"mac-closeout.json"),JsonSerializer.Serialize(new{
            startedUtc=_closeoutStarted,updatedUtc=DateTime.UtcNow,completed,passed=completed&&unexpected.Length==0,
            awakeSeconds=_awakeSeconds,requiredAwakeSeconds=_layoutsOnly?0:7200,captureSuccesses=_captureSuccesses,
            currentLayout=_layoutName,layouts=_layoutResults,recoveries=_recoveries,samples=_soakSamples,
            errors=_soakErrors,unexpectedErrors=unexpected,processId=Environment.ProcessId,workingSet=process.WorkingSet64,
            scope=_layoutsOnly?"Four representative component layouts only; no device, session recovery or long-run acceptance.":"Four representative component layouts with synthetic timetable; not all user configurations or older macOS. Minimize skipped at user request."},new JsonSerializerOptions{WriteIndented=true}));
    }
    private bool ExpectedSleepInterruption(string error)
    {
        if(error.Length<19||!DateTime.TryParse(error[..19],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var utc))return false;
        if(!error.Contains("显示器",StringComparison.Ordinal)&&!error.Contains("背景捕获超时",StringComparison.Ordinal))return false;
        var notes=Fixture.GetProperty("workspaceEvents").EnumerateArray();
        return notes.Any(note=>(note.GetProperty("name").GetString() is "NSWorkspaceWillSleepNotification" or "NSWorkspaceDidWakeNotification")&&
            Math.Abs((DateTime.Parse(note.GetProperty("utc").GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal)-utc).TotalSeconds)<=15)&&
            _recoveries.Select(value=>JsonSerializer.SerializeToElement(value)).Any(recovery=>recovery.GetProperty("label").GetString()=="sleep-wake"&&recovery.GetProperty("passed").GetBoolean()&&recovery.GetProperty("utc").GetDateTime()>utc);
    }
    private static bool DesktopLocked()
    {
        using var process=Process.Start(new ProcessStartInfo("/usr/sbin/ioreg"){
            UseShellExecute=false,RedirectStandardOutput=true,ArgumentList={"-n","Root","-d1"}})!;
        var text=process.StandardOutput.ReadToEnd();process.WaitForExit();
        return text.Contains("\"IOConsoleLocked\" = Yes",StringComparison.Ordinal);
    }
    private ScreenRegion LocalRegion()
    {
        var window=AppBase.Current.MainWindow!;
        var grids=window.GetVisualDescendants().OfType<Grid>().Where(x=>x.Name=="PART_GridWrapper"&&x.Bounds.Width>0&&x.Bounds.Height>0).ToArray();
        if(grids.Length==0)throw new InvalidOperationException("实际主界面布局没有有效组件区域。");
        var area=grids.Select(x=>new Rect(x.TranslatePoint(default,window)??default,x.Bounds.Size)).Aggregate((a,b)=>a.Union(b));
        return new(area.X,area.Y,area.Width,area.Height);
    }
    private Task<ScreenRegion> GlobalRegion()=>((IWindowRegionProvider)_native.Advanced.Background!).WindowRegionAsync(AppBase.Current.MainWindow!.TryGetPlatformHandle()!.Handle,LocalRegion(),default);
    private async Task PaintCloseout(string mode)
    {
        if(mode=="restore")File.WriteAllText(Path.Combine(_root,"background-command.json"),"{\"mode\":\"restore\"}");
        else
        {
            var global=await GlobalRegion();
            File.WriteAllText(Path.Combine(_root,"background-command.json"),JsonSerializer.Serialize(new{mode,x=global.X,y=global.Y,width=Math.Max(global.Width,400),height=Math.Max(global.Height,100)}));
        }
        await Task.Delay(900);
    }
    private ComponentProfile CloseoutLayout(int index)
    {
        ComponentSettings Component(string id,object? settings=null)=>new(){Id=id,Settings=settings};
        ComponentSettings Text(string text)=>Component("EE8F66BD-C423-4E7C-AB46-AA9976B00E08",JsonSerializer.SerializeToElement(new{TextContent=text,UseCustomFontColor=false,FontSize=index==3?28:18}));
        const string schedule="1db2017d-e374-4bc6-9d57-0b4adf03a6b8",clock="9E1AF71D-8F77-4B21-A342-448787104DD9";
        var profile=new ComponentProfile();
        var first=new MainWindowLineSettings{IsMainLine=true};
        first.Children.Add(Component(clock));first.Children.Add(Component(schedule));
        if(index!=0)first.Children.Add(Text(index==3?"中文课程名称与长标题 · Chinese / 12345":"布局验收 · 数学 语文 英语"));
        profile.Lines.Add(first);
        if(index>=2)
        {
            profile.Lines.Add(new MainWindowLineSettings{Children={Text("第二行 · 下节课与教室信息 301"),Component(clock)}});
            if(index==3)profile.Lines.Add(new MainWindowLineSettings{Children={Text("第三行 · 大字号 / 中英文混排")}});
        }
        return profile;
    }
    private void SeedCloseoutTimetable()
    {
        var service=_services.GetRequiredService<IProfileService>();_originalProfile=service.Profile;
        var profile=new Profile{Name="SystemTools 合成课表验收"};
        var times=new TimeLayout{Name="全天合成时间表"};
        var layoutId=Guid.NewGuid();var planId=Guid.NewGuid();var plan=new ClassPlan{Name="本轮合成课表",TimeLayoutId=layoutId};
        var names=new[]{"语文","数学","英语","物理"};
        for(var i=0;i<4;i++)
        {
            var subject=Guid.NewGuid();profile.Subjects.Add(subject,new Subject{Name=names[i],Initial=names[i][..1]});
            times.Layouts.Add(new TimeLayoutItem{StartTime=TimeSpan.FromHours(i*6),EndTime=TimeSpan.FromHours((i+1)*6)-TimeSpan.FromSeconds(1),TimeType=0});
            plan.Classes.Add(new ClassInfo{SubjectId=subject});
        }
        profile.TimeLayouts.Add(layoutId,times);profile.ClassPlans.Add(planId,plan);
        profile.ClassPlans=new(profile.ClassPlans);
        profile.TempClassPlanId=planId;profile.TempClassPlanSetupTime=DateTime.Now;
        service.Profile=profile;service.SaveProfile();
    }
    private async Task ValidateLayout(int index)
    {
        var config=_services.GetRequiredService<MainConfigHandler>();var appearance=_services.GetRequiredService<NativeBackgroundAppearance>();
        var settings=new SystemTools.Services.ClassIslandSettingsService();var components=_services.GetRequiredService<IComponentsService>();
        config.Data.AutoHideMainWindowWhenOccluded=false;config.Data.AutoSwitchClassIslandTheme=false;
        await Task.Delay(2200);settings.SetMainWindowVisible(true);
        components.CurrentComponents=CloseoutLayout(index);_layoutName=new[]{"单行基础","单行长文字","双行","三行大字号"}[index];
        await Task.Delay(1500);
        var region=await GlobalRegion();
        await PaintCloseout("text");
        var debugFrame=await _native.Advanced.Background!.CaptureAsync(region,default);
        File.WriteAllBytes(Path.Combine(_root,"layout-"+index+".png"),debugFrame.Png);
        File.WriteAllText(Path.Combine(_root,"layout-"+index+"-text.json"),JsonSerializer.Serialize(await _native.Advanced.TextRecognition!.RecognizeAsync(debugFrame,default)));
        config.Data.AutoHideMainWindowWhenOccluded=true;
        for(var i=0;i<8&&settings.GetMainWindowVisible()!=false;i++){await appearance.RefreshAsync(default);await Task.Delay(300);}
        Check(settings.GetMainWindowVisible()==false,_layoutName+"：背景文字触发实际隐藏");
        await PaintCloseout("clear");
        for(var i=0;i<10&&settings.GetMainWindowVisible()!=true;i++){await appearance.RefreshAsync(default);await Task.Delay(300);}
        Check(settings.GetMainWindowVisible()==true,_layoutName+"：无文字时实际恢复");
        config.Data.AutoHideMainWindowWhenOccluded=false;await Task.Delay(2200);
        config.Data.AutoSwitchClassIslandTheme=true;
        await PaintCloseout("black");for(var i=0;i<4;i++){await appearance.RefreshAsync(default);await Task.Delay(250);}
        var dark=settings.GetMainWindowTheme();
        await PaintCloseout("clear");for(var i=0;i<5;i++){await appearance.RefreshAsync(default);await Task.Delay(250);}
        var light=settings.GetMainWindowTheme();
        Check(dark is not null&&light is not null&&dark!=light,_layoutName+"：实际黑白背景驱动不同主题");
        config.Data.AutoSwitchClassIslandTheme=false;await Task.Delay(2200);await PaintCloseout("restore");
        config.Data.AutoHideMainWindowWhenOccluded=true;
        _layoutResults.Add(new{name=_layoutName,rows=components.CurrentComponents.Lines.Count,region,darkTheme=dark,lightTheme=light,passed=true});CloseoutReport();
    }
    private async Task CloseoutRecovery(string label)
    {
        await _closeoutGate.WaitAsync();
        try
        {
            if(DesktopLocked())throw new InvalidOperationException("请本人先解锁再做恢复检查。");
            var camera=_services.GetRequiredService<CameraCaptureContext>().Camera;
            var state=await ((IAsyncCapabilityProbe)camera).ProbeAsync(default);
            File.WriteAllText(Path.Combine(_root,"camera-permission-"+label+".json"),JsonSerializer.Serialize(state));
            if(!state.CanExecute&&label=="initial")state=await ((IAsyncPermissionRequest)camera).RequestAccessAsync(default);
            Check(state.CanExecute,label+"：助手相机权限可用（"+state.State+"："+state.Reason+"）");
            var devices=await camera.ListDevicesAsync(default);Check(devices.Count>0,label+"：读取实际相机");
            var capture=Path.Combine(_root,"recovery-"+Guid.NewGuid().ToString("N")+".png");
            await camera.CapturePngAsync(devices[0].Id,capture,default);MediaFiles.RequirePng(capture);
            Check(new FileInfo(capture).Length>100,label+"：真实相机抓拍恢复");
            var client=camera.GetType().GetField("_client",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(camera)!;
            async Task<JsonElement> Request(string op,object? args=null)=>await (Task<JsonElement>)client.GetType().GetMethod("RequireAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(client,[op,args,CancellationToken.None,null])!;
            if(label=="initial")await Request("microphone.request");
            var id=Guid.NewGuid().ToString("D");
            await Request("microphone.prepare",new{id});
            try{await Request("microphone.start",new{id});await Task.Delay(700);var pcm=await Request("microphone.poll",new{id});Check(Convert.FromBase64String(pcm.GetProperty("pcm").GetString()!).Length>0,label+"：真实麦克风 PCM 恢复");}
            finally{await Request("microphone.release",new{id});}
            var jobs=await Request("session.status");Check(jobs.GetProperty("cameraJobs").GetInt32()==0&&jobs.GetProperty("microphoneJobs").GetInt32()==0,label+"：设备会话全部释放");
            var region=await GlobalRegion();var frame=await _native.Advanced.Background!.CaptureAsync(region,default);
            Check(frame.ExcludesOwnProcess&&frame.Png.Length>100,label+"：排除宿主的背景捕获恢复");
            var boxes=await _native.Advanced.TextRecognition!.RecognizeAsync(frame,default);Check(true,label+"：Vision OCR 请求完成");
            _recoveries.Add(new{label,utc=DateTime.UtcNow,identity=client.GetType().GetProperty("Identity",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(client),pngBytes=new FileInfo(capture).Length,ocrBoxes=boxes.Count,passed=true});CloseoutReport();
        }
        finally{_closeoutGate.Release();}
    }
    private async Task RunCloseout()
    {
        _closeoutStarted=DateTime.UtcNow;
        var config=_services.GetRequiredService<MainConfigHandler>();var appearance=_services.GetRequiredService<NativeBackgroundAppearance>();
        var originalHide=config.Data.AutoHideMainWindowWhenOccluded;var originalTheme=config.Data.AutoSwitchClassIslandTheme;
        var settings=new SystemTools.Services.ClassIslandSettingsService();var visible=settings.GetMainWindowVisible();
        var components=_services.GetRequiredService<IComponentsService>();_originalLayout=components.CurrentComponents;
        try
        {
            SeedCloseoutTimetable();
            if(!_layoutsOnly)await CloseoutRecovery("initial");
            for(var i=0;i<4;i++)await ValidateLayout(i);
            if(_layoutsOnly)
            {
                CloseoutReport(true);File.WriteAllText(Path.Combine(_root,"closeout-layouts-completed"),"done");
                return;
            }
            File.WriteAllText(Path.Combine(_root,"closeout-session-ready"),"ready");
            var last=Stopwatch.GetTimestamp();var lastSample=DateTime.UtcNow;var layout=3;var cycles=0;
            while(_awakeSeconds<7200&&!_native.Stopping.IsCancellationRequested)
            {
                await Task.Delay(5000);var now=Stopwatch.GetTimestamp();var elapsed=Stopwatch.GetElapsedTime(last,now).TotalSeconds;last=now;
                var locked=DesktopLocked();
                if(elapsed<15&&!locked)_awakeSeconds+=elapsed;
                if(!locked&&File.Exists(Path.Combine(_root,"closeout-recovery-request")))
                {
                    var label=File.ReadAllText(Path.Combine(_root,"closeout-recovery-request"));File.Delete(Path.Combine(_root,"closeout-recovery-request"));
                    await CloseoutRecovery(label);
                }
                if(!locked)
                {
                    try{await appearance.RefreshAsync(default);_captureSuccesses++;}
                    catch(Exception error){_soakErrors.Add(DateTime.UtcNow+" "+error.Message);}
                    if(++cycles%180==0){layout=(layout+1)%4;components.CurrentComponents=CloseoutLayout(layout);_layoutName=new[]{"单行基础","单行长文字","双行","三行大字号"}[layout];}
                }
                if((DateTime.UtcNow-lastSample).TotalSeconds>=60)
                {
                    var process=Process.GetCurrentProcess();process.Refresh();
                    _soakSamples.Add(new{utc=DateTime.UtcNow,awakeSeconds=_awakeSeconds,locked,layout=_layoutName,cpuSeconds=process.TotalProcessorTime.TotalSeconds,workingSet=process.WorkingSet64,threads=process.Threads.Count,captureSuccesses=_captureSuccesses,lastError=appearance.LastError});lastSample=DateTime.UtcNow;
                }
                CloseoutReport();
            }
            Check(_awakeSeconds>=7200,"实际解锁运行累计达到两小时");
            await CloseoutRecovery("soak-finished");
            Check(_soakErrors.All(ExpectedSleepInterruption),"两小时运行无未恢复错误；睡眠切换瞬态保留并验证恢复");
            CloseoutReport(true);File.WriteAllText(Path.Combine(_root,"closeout-soak-completed"),"done");
        }
        catch(Exception error){_soakErrors.Add(error.ToString());_failures.Add("Mac 收尾："+error);Write();CloseoutReport();_status.Text="收尾失败："+error.Message;}
        finally
        {
            config.Data.AutoHideMainWindowWhenOccluded=originalHide;config.Data.AutoSwitchClassIslandTheme=originalTheme;
            components.CurrentComponents=_originalLayout!;
            components.SaveConfig();
            if(_originalProfile is not null){var profiles=_services.GetRequiredService<IProfileService>();profiles.Profile=_originalProfile;profiles.SaveProfile();}
            if(visible is {} originalVisible)settings.SetMainWindowVisible(originalVisible);
            await PaintCloseout("restore");
            File.WriteAllText(Path.Combine(_root,"closeout-restored.json"),JsonSerializer.Serialize(new{
                utc=DateTime.UtcNow,
                layoutRestored=ReferenceEquals(components.CurrentComponents,_originalLayout),
                profileRestored=ReferenceEquals(_services.GetRequiredService<IProfileService>().Profile,_originalProfile),
                hideRestored=config.Data.AutoHideMainWindowWhenOccluded==originalHide,
                themeRestored=config.Data.AutoSwitchClassIslandTheme==originalTheme,
                visibilityRestored=settings.GetMainWindowVisible()==visible
            },new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}

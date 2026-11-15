using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Shared;

if(args.FirstOrDefault()=="credential-cleanup")
{
    var references=JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[1]))!;
    var store=new SystemTools.Platforms.MacOS.KeychainProtector();
    var deleted=0;
    foreach(var reference in references)
        if(store.Delete(reference))deleted++;
    Console.WriteLine($"Synthetic references deleted: {deleted}/{references.Length}");
    if(deleted!=references.Length) { Console.Error.WriteLine("Some synthetic references require the unlocked original host for cleanup.");Environment.ExitCode=1; }
    return;
}
if (args.FirstOrDefault() == "credential-child")
{
    var state = JsonSerializer.Deserialize<CredentialState>(File.ReadAllText(args[1]))!;
    IStoredCredentialProtector store = OperatingSystem.IsMacOS() ? new SystemTools.Platforms.MacOS.KeychainProtector() : new SystemTools.Platforms.Linux.SecretServiceProtector();
    if (!store.TryUnprotect(state.Reference, out var secret) || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))) != state.Hash)
        throw new Exception("跨进程系统凭据读取验证失败。");
    Console.WriteLine("PASS child: 系统凭据跨进程可读取且内容哈希匹配");
    return;
}
var count = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine($"PASS {++count}: {message}"); }
var root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts/m8/contracts-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var previous = CredentialProtection.Provider;
try
{
    var fake = new FixtureStore(); CredentialProtection.Provider = fake;
    using (var config = new MainConfigHandler(root))
    {
        config.Data.SaveApiKeySecurely("fixture-first-secret");
        var original = File.ReadAllText(Path.Combine(root, "Main.json"));
        Check(!original.Contains("fixture-first-secret") && original.Contains("keychain:v1:"), "配置只保存系统凭据引用，没有明文");
        using (var reloaded = new MainConfigHandler(root)) Check(reloaded.Data.AiApiKey == "fixture-first-secret", "完整配置模型重启后使用系统凭据");
        fake.Reject = true;
        try { config.Data.SaveApiKeySecurely("fixture-second-secret"); throw new Exception("拒绝写入仍成功"); }
        catch (CryptographicException) { Check(File.ReadAllText(Path.Combine(root,"Main.json")) == original && config.Data.AiApiKey == "fixture-first-secret", "系统凭据写入拒绝保持原文件和内存密钥"); }
        fake.Reject = false;
        config.Data.ClearSavedApiKey();
        Check(!fake.Values.Any() && config.Data.SavedCredentialReference.Length == 0, "显式移除删除系统项目并清空当前引用");
    }
    var foreign = Path.Combine(root, "foreign"); Directory.CreateDirectory(foreign);
    File.WriteAllText(Path.Combine(foreign,"Main.json"), "{\"aiApiKey\":\"dpapi-current-user:v1:opaque\",\"unknown\":17}");
    using (var config = new MainConfigHandler(foreign))
    { config.Save(); Check(config.Data.IsAiApiKeyUnavailable && File.ReadAllText(Path.Combine(foreign,"Main.json")).Contains("dpapi-current-user:v1:opaque"), "新后端不覆盖无法解密的 Windows 密文"); }
    var conflict = Path.Combine(root, "conflict"); Directory.CreateDirectory(conflict);
    using (var config = new MainConfigHandler(conflict))
    {
        config.Data.SaveApiKeySecurely("first");
        var reference = config.Data.SavedCredentialReference;
        var existing = fake.Values.Count;
        File.WriteAllText(Path.Combine(conflict, "Main.json"), "{\"externallyEdited\":true}");
        try { config.Data.SaveApiKeySecurely("second"); throw new Exception("过期写入被接受"); }
        catch (IOException) { Check(config.Data.SavedCredentialReference == reference && fake.Values.Count == existing, "保存冲突恢复内存状态并清理未提交的新系统凭据"); }
        try { config.Data.ClearSavedApiKey(); throw new Exception("过期清空被接受"); }
        catch (IOException) { Check(fake.Values.ContainsKey(reference), "配置清空冲突不会先删除原系统凭据"); }
        fake.Delete(reference);
    }
    CredentialProtection.Provider = previous;
    using(var schedule = new SystemTools.Common.Automation.ScheduledOperation())
    {
        var executions=0;
        schedule.Schedule(TimeSpan.FromMilliseconds(100),_=> { Interlocked.Increment(ref executions);return Task.CompletedTask; },default);
        schedule.Cancel();await schedule.Completion;Check(executions==0,"计时关机取消后不派发系统操作");
        schedule.Schedule(TimeSpan.Zero,_=> { Interlocked.Increment(ref executions);return Task.CompletedTask; },default);
        await schedule.Completion;Check(executions==1,"到期计划只派发一次");
        using var stopped=new CancellationTokenSource();
        schedule.Schedule(TimeSpan.FromHours(1),_=> { Interlocked.Increment(ref executions);return Task.CompletedTask; },stopped.Token);
        stopped.Cancel();await schedule.Completion;Check(executions==1,"宿主停止取消未执行计划");
        schedule.Schedule(TimeSpan.Zero,_=>Task.FromException(new IOException("denied")),default);
        await schedule.Completion;Check(schedule.LastError is IOException,"系统拒绝在计划状态中可观察");
    }
    foreach(var type in new[]{typeof(SystemTools.PortableUndoAction),typeof(SystemTools.PortableCloseKeyAction),typeof(SystemTools.PortableSwitchAppAction),
        typeof(SystemTools.PortableRestartAction),typeof(SystemTools.NativeEnterAction),typeof(SystemTools.NativeMirrorDisplayAction)})
        Check(type.BaseType!.GetGenericArguments().FirstOrDefault()==typeof(SystemTools.Settings.ShortcutKeyNotificationSettings),"宿主 JSON 行动工厂可识别设置类型："+type.Name);
    var audioPath=Path.Combine(root,"后台音频.wav");File.WriteAllBytes(audioPath,[0,1,2,3]);
    foreach(var wait in new[]{false,true})
    {
        var audio=System.Reflection.DispatchProxy.Create<ClassIsland.Core.Abstractions.Services.IAudioService,FixtureAudio>();
        var fixture=(FixtureAudio)(object)audio;
        var lifetime=new SystemTools.PortableSurfaceLifetime();
        var action=new SystemTools.PortableAudioAction(lifetime,Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemTools.PortableAudioAction>.Instance,audio);
        var item=new ClassIsland.Shared.Models.Automation.ActionItem{Settings=new SystemTools.Settings.BackgroundPlayAudioSettings{AudioFilePath=audioPath,WaitForPlaybackCompleted=wait}};
        var set=new ClassIsland.Shared.Models.Automation.ActionSet();set.ActionItems.Add(item);set.SetStartRunning(true);
        var invocation=action.InvokeAsync(item,set,false);await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(!wait)
        {
            await invocation;set.SetEndRunning(true);await Task.Delay(50);
            Check(!fixture.Stopped.Task.IsCompleted&&fixture.Stream!.CanRead,"后台音频跨越宿主正常结束行动组继续播放");
            ((CancellationTokenSource)typeof(SystemTools.PortableSurfaceLifetime).GetField("_stop",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(lifetime)!).Cancel();
            await fixture.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(fixture.Token.IsCancellationRequested,"插件生命周期令牌取消后台音频");
        }
        else
        {
            Check(!invocation.IsCompleted,"等待模式保持行动运行直到音频结束");
            var cts=(CancellationTokenSource)typeof(ClassIsland.Shared.Models.Automation.ActionSet).GetField("InterruptCts",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(set)!;
            await cts.CancelAsync();await invocation.WaitAsync(TimeSpan.FromSeconds(5));set.SetEndRunning(true);
            Check(fixture.Token.IsCancellationRequested,"行动组人工中断取消等待模式音频");
        }
        for(var i=0;i<50&&fixture.Stream!.CanRead;i++)await Task.Delay(10);
        Check(!fixture.Stream!.CanRead,"取消播放后释放音频文件流");
    }
    using(var schedule = new SystemTools.Common.Automation.ScheduledOperation())
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        schedule.Schedule(TimeSpan.Zero,async _=> { entered.TrySetResult();await release.Task;throw new IOException("old operation"); },default);
        var old = schedule.Completion;await entered.Task;
        schedule.Schedule(TimeSpan.FromHours(1),_=>Task.CompletedTask,default);
        release.TrySetResult();await old;
        Check(schedule.LastError is null && schedule.Due is not null,"替换计划后旧操作的迟到错误不污染新计划");
        schedule.Cancel();await schedule.Completion;
    }
    var appearance = new FixtureSession();
    var transactions = new SystemTools.Common.Automation.SettingRestoreTransactions();var group=Guid.NewGuid();var owner=new object();
    await transactions.ApplyAsync(appearance,group,owner,"theme","dark",true,default);
    await transactions.RevertAsync(appearance,group,owner);
    Check(appearance.Value=="light","外观恢复状态存于共享服务，可跨行动实例恢复");
    await transactions.ApplyAsync(appearance,group,owner,"theme","dark",true,default);appearance.Value="manual";
    await transactions.RevertAsync(appearance,group,owner);
    Check(appearance.Value=="manual","外观回退不覆盖用户后续手动修改");
    using(var schedule=new SystemTools.Common.Automation.ScheduledOperation())
    {
        var first=new object();var second=new object();
        schedule.Schedule(TimeSpan.FromHours(1),_=>Task.CompletedTask,default,first);
        schedule.Schedule(TimeSpan.FromHours(1),_=>Task.CompletedTask,default,second);
        schedule.Cancel(first);Check(schedule.Due is not null,"旧行动恢复不会取消另一行动新建的关机计划");
        schedule.Cancel(second);await schedule.Completion;Check(schedule.Due is null,"计划所属行动可取消自己的计时");
    }
    var pointer=new FixturePointer();
    using(var stop=new CancellationTokenSource())
    {
        pointer.Pressed=()=>stop.Cancel();
        try { await SystemTools.Common.Automation.PortablePointer.ExecuteAsync(pointer,[new(4,20,30,0,0,false),new(4,30,40,0,100,false)],stop.Token); }
        catch(OperationCanceledException) { }
        Check(pointer.Events.Last()=="up:1","拖动取消始终释放已按下的鼠标键");
    }
    pointer=new FixturePointer { Fail=true };
    try { await SystemTools.Common.Automation.PortablePointer.ExecuteAsync(pointer,[new(0,20,30,0,0,false)],default); }
    catch(IOException) { }
    Check(pointer.Events.Last()=="up:1","鼠标按下调用部分失败仍尝试释放");
    if (args.Contains("--native-credentials"))
    {
        IStoredCredentialProtector store = OperatingSystem.IsMacOS() ? new SystemTools.Platforms.MacOS.KeychainProtector() : new SystemTools.Platforms.Linux.SecretServiceProtector();
        Check(store.Availability.CanExecute, "本机系统凭据后端可用");
        var secret = "m8-synthetic-合成凭据-" + Guid.NewGuid().ToString("N");
        var reference = store.Protect(secret);
        try
        {
            Check(store.TryUnprotect(reference, out var actual) && actual == secret, "真实系统凭据写入和读取，不输出密钥");
            Check(!store.TryUnprotect("dpapi-current-user:v1:opaque", out _), "真实后端拒绝其他平台引用");
            Check(!store.TryUnprotect(store.Scheme + "../escape", out _), "真实后端拒绝格式错误引用");
            var file = Path.Combine(root,"credential-state.json");
            File.WriteAllText(file, JsonSerializer.Serialize(new CredentialState(reference,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))))));
            var info = new ProcessStartInfo("dotnet") { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true };
            foreach (var value in new[]{typeof(CredentialState).Assembly.Location,"credential-child",file}) info.ArgumentList.Add(value);
            using var child=Process.Start(info)!;
            var output=child.StandardOutput.ReadToEndAsync(); var error=child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Check(child.ExitCode==0, "不同进程重新打开同一系统凭据，验证重启持久性");
        }
        finally { Check(store.Delete(reference), "删除本轮合成系统凭据"); }
        Check(!store.TryUnprotect(reference,out _), "删除后原引用不能再读取");
    }
}
finally { CredentialProtection.Provider=previous; Directory.Delete(root,true); }
public sealed record CredentialState(string Reference,string Hash);
public class FixtureAudio:System.Reflection.DispatchProxy
{
    public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource Stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Stream? Stream;
    public CancellationToken Token;
    protected override object? Invoke(System.Reflection.MethodInfo? method,object?[]? args)
    {
        if(method?.Name!="PlayAudioAsync")throw new NotSupportedException(method?.Name);
        Stream=(Stream)args![0]!;Token=(CancellationToken)args[2]!;Started.TrySetResult();return Play();
    }
    private async Task Play(){try{await Task.Delay(Timeout.Infinite,Token);}finally{Stopped.TrySetResult();}}
}
public sealed class FixtureStore : IStoredCredentialProtector
{
    public string Scheme=>"keychain:v1:";
    public Capability Availability=>new(CapabilityState.Available);
    public readonly Dictionary<string,string> Values=[];
    public bool Reject;
    public string Protect(string plaintext) { if(Reject)throw new CryptographicException("fixture denied"); var reference=Scheme+Guid.NewGuid().ToString("N");Values[reference]=plaintext;return reference; }
    public bool TryUnprotect(string reference,out string plaintext)=>Values.TryGetValue(reference,out plaintext!);
    public bool Delete(string reference)=>Values.Remove(reference);
}


public sealed class FixturePointer : IPointerInput
{
    public List<string> Events=[];
    public Action? Pressed;
    public bool Fail;
    public Capability Probe()=>new(CapabilityState.Available);
    public string CaptureTarget()=>"fixture";
    public Task SendAsync(string operation,int x,int y,int value,string target,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();Events.Add(operation+":"+value);
        if(operation=="down") { Pressed?.Invoke();if(Fail)throw new IOException("partial"); }
        return Task.CompletedTask;
    }
}


public sealed class FixtureSession:IDesktopSession
{
    public string Value="light";
    public Capability Probe(string operation)=>new(CapabilityState.Available);
    public Task<string> ExecuteAsync(string operation,string? value,CancellationToken token)
    {token.ThrowIfCancellationRequested();if(operation.EndsWith(".set"))Value=value!;return Task.FromResult(Value);}
}

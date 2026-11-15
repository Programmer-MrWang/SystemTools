using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SystemTools.Common.Media;
using SystemTools.Contracts;

static class MediaConcurrencyTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(SpeechInputRouter.Score("你好，ＣＩ！", "你好ci") == 1, "关键词按 Unicode 规范化匹配");
        check(ConversationTurns.IsPlaybackEcho("天气晴朗", "今天的天气晴朗") &&
              !ConversationTurns.IsPlaybackEcho("请停止", "今天的天气晴朗"), "朗读回声与新的打断内容分别处理");
        var parsed = VoskDecoder.Parse("""{"text":"hello","result":[{"conf":0.8},{"conf":1.0}]}""", true);
        check(parsed is { IsFinal: true } && Math.Abs(parsed.Confidence!.Value - 0.9) < 0.001, "Vosk 结果保留真实置信度");
        var lyrics = new LrcTimeline("[offset:500]\n[00:01.00]first\n[00:02.00][00:04.00]second\n[00:99.00]invalid");
        check(lyrics.At(TimeSpan.FromSeconds(1)) == "" && lyrics.At(TimeSpan.FromSeconds(1.6)) == "first" &&
            lyrics.At(TimeSpan.FromSeconds(4.5)) == "second", "歌词按真实进度、偏移与多时间标签定位，忽略无效时间");
        var unsupportedAuth = await new UnsupportedAuthentication().VerifyAsync("test", 0, default);
        check(!unsupportedAuth.Verified, "Linux 未支持的认证后端始终拒绝放行");
        var backend = new FakeSpeech();
        var router = new SpeechInputRouter(backend, default);
        var triggered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = router.RegisterKeyword("hello ci", 0.9, () => triggered.TrySetResult());
        await WaitAsync(() => backend.Starts == 1);
        backend.Current!.Writer.TryWrite(new("hello ci", true));
        await triggered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        check(true, "后台关键词从真实识别流触发一次回调");
        using var stop = new CancellationTokenSource();
        await using (var foreground = router.ListenAsync(stop.Token).GetAsyncEnumerator())
        {
            var first = foreground.MoveNextAsync().AsTask();
            await WaitAsync(() => backend.Starts >= 2);
            check(backend.MaximumActive == 1, "前台听写等待后台采集完全结束后才打开麦克风");
            backend.Current!.Writer.TryWrite(new("dictation", true));
            check(await first && foreground.Current.Text == "dictation", "前台听写收到自己的识别流");
            await using var conflict = router.ListenAsync(default).GetAsyncEnumerator();
            try { await conflict.MoveNextAsync(); throw new Exception("Concurrent foreground accepted"); }
            catch (NativeFeatureException) { check(true, "两个前台对话不会同时占用麦克风"); }
            stop.Cancel();
            try { await foreground.MoveNextAsync(); }
            catch (OperationCanceledException) { }
        }
        await WaitAsync(() => backend.Starts >= 3);
        check(backend.MaximumActive == 1, "听写取消后后台关键词恢复且没有双开");
        registration.Dispose();
        await router.DisposeAsync();
        check(backend.Active == 0, "卸载关键词与停止宿主后零采集会话");

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<string>();
        await using var turns = new ConversationTurns(default);
        var old = turns.SubmitAsync("old", async (_, _) => { firstStarted.SetResult(); return await oldResponse.Task; },
            (_, _) => Task.CompletedTask, published.Add);
        await firstStarted.Task;
        var latest = turns.SubmitAsync("new", (_, _) => Task.FromResult("new reply"), (_, _) => Task.CompletedTask, published.Add);
        oldResponse.SetResult("stale reply");
        try { await old; } catch (OperationCanceledException) { }
        await latest;
        check(published.SequenceEqual(["new reply"]), "新一轮打断旧请求，忽略其迟到回复");

        var speaking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var spoken = turns.SubmitAsync("speak", (_, _) => Task.FromResult("reply"), async (_, token) =>
        { speaking.SetResult(); await Task.Delay(Timeout.Infinite, token); }, _ => { });
        await speaking.Task;
        turns.Interrupt();
        try { await spoken; } catch (OperationCanceledException) { }
        check(spoken.IsCanceled, "打断同时取消当前朗读，不遗留后台播报任务");
    }

    private static async Task WaitAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeSpeech : ISpeechRecognition
    {
        public int Starts;
        public int Active;
        public int MaximumActive;
        public Channel<SpeechSegment>? Current;
        public Capability Probe() => new(CapabilityState.Available);
        public async IAsyncEnumerable<SpeechSegment> RecognizeAsync([EnumeratorCancellation] CancellationToken token)
        {
            var channel = Channel.CreateUnbounded<SpeechSegment>();
            Current = channel;
            var active = Interlocked.Increment(ref Active);
            MaximumActive = Math.Max(MaximumActive, active);
            Interlocked.Increment(ref Starts);
            try { await foreach (var item in channel.Reader.ReadAllAsync(token)) yield return item; }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
}

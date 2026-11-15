using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools.Platforms.Windows;

internal static class WindowsM5ConversationAcceptance
{
    internal static async Task RunAsync(IServiceProvider provider, Action<bool, string> check, CancellationToken token)
    {
        var config = GlobalConstants.MainConfig!;
        var original = (config.Data.EnableAiService, config.Data.AiApiUrl, config.Data.AiModel);
        var ai = (OpenAiCompatibleService)provider.GetRequiredService<IOpenAiCompatibleService>();
        var oldKey = ai.SessionApiKey;
        const string fixtureKey = "systemtools-m5-session-only-fixture";
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var source = Path.Combine(GlobalConstants.PluginConfigFolder!, "M5-ai-source.txt");
        var target = Path.Combine(GlobalConstants.PluginConfigFolder!, "M5-ai-must-not-exist.txt");
        File.WriteAllText(source, "isolated approval fixture");
        var calls = 0;
        var denied = 0;
        var authenticated = true;
        var releaseLateReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateReplyFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stop.Token);
                using var requestReader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                using var request = JsonDocument.Parse(await requestReader.ReadToEndAsync(stop.Token));
                authenticated &= context.Request.Headers["Authorization"] == "Bearer " + fixtureKey;
                var messages = request.RootElement.GetProperty("messages");
                var last = messages[messages.GetArrayLength() - 1];
                if (last.GetProperty("role").GetString() == "tool")
                {
                    using var output = JsonDocument.Parse(last.GetProperty("content").GetString()!);
                    var outcome = output.RootElement.GetProperty("status").GetString();
                    if (outcome == "denied") Interlocked.Increment(ref denied);
                    else if (outcome != "success") throw new InvalidOperationException("Fixture tool failed: " + output.RootElement);
                }
                var round = Interlocked.Increment(ref calls);
                var name = round switch { 1 => "list_classisland_actions", 2 => "describe_classisland_actions", _ => "execute_classisland_actions" };
                object arguments = round switch
                {
                    1 => new { query = "SystemTools.Copy" },
                    2 => new { actionIds = new[] { "SystemTools.Copy" } },
                    _ => new { summary = "隔离验收：请求复制夹具文件", actions = new[] { new { id = "SystemTools.Copy", settings = new { operationType = "文件", sourcePath = source, destinationPath = target } } } }
                };
                if (round == 6) await releaseLateReply.Task.WaitAsync(stop.Token);
                object message = round <= 4
                    ? new { role = "assistant", content = "", tool_calls = new[] { new { id = "fixture-" + round, type = "function", function = new { name, arguments = JsonSerializer.Serialize(arguments) } } } }
                    : new { role = "assistant", content = round == 6 ? "M5_LATE_REPLY_MUST_BE_DISCARDED" : "M5_APPROVAL_REJECTED" };
                var body = JsonSerializer.SerializeToUtf8Bytes(new { id = "m5-fixture", model = "m5-fixture", choices = new[] { new { index = 0, message, finish_reason = round <= 4 ? "tool_calls" : "stop" } } });
                context.Response.ContentType = "application/json";
                if (round == 7) context.Response.StatusCode = 503;
                context.Response.ContentLength64 = body.Length;
                try { await context.Response.OutputStream.WriteAsync(body, stop.Token); }
                catch (HttpListenerException) when (round == 6) { }
                catch (IOException) when (round == 6) { }
                finally { context.Response.Close(); if (round == 6) lateReplyFinished.TrySetResult(); }
            }
        }, stop.Token);
        NativeVoiceService? voice = null;
        Window? window = null;
        try
        {
            config.Data.EnableAiService = true; config.Data.AiApiUrl = $"http://127.0.0.1:{port}/v1"; config.Data.AiModel = "m5-fixture";
            ai.SessionApiKey = fixtureKey;
            voice = ActivatorUtilities.CreateInstance<NativeVoiceService>(provider);
            await voice.OpenAsync(listen: false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                window = Windows().Single(item => item.Title == "SystemTools 语音对话");
                check(window.IsVisible && window.GetVisualDescendants().OfType<TextBox>().Any(item => item.Name == "MessageInput"), "共享对话窗口在真实 Windows 宿主显示");
                window.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "MessageInput").Text = "请复制夹具文件";
                window.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "SendMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            var approvals = 0;
            var completed = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            while (!completed)
            {
                if (server.IsFaulted) await server;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var dialog in Windows().Where(item => item.Title == "确认本次操作").ToArray())
                    {
                        approvals++;
                        dialog.GetVisualDescendants().OfType<Button>().Single(item => Equals(item.Content, "取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    completed = window!.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "ConversationTranscript").Text?.Contains("M5_APPROVAL_REJECTED", StringComparison.Ordinal) == true;
                });
                if (!completed) await Task.Delay(50, timeout.Token);
            }
            check(calls == 5 && authenticated, "真实兼容 HTTP 客户端完成五轮目录、契约、执行与拒绝结果交换");
            check(approvals == 1 && denied == 2 && !File.Exists(target), "本地审批拒绝后不执行且同轮再次执行不重复弹窗");
            async Task SendAsync(string text) => await Dispatcher.UIThread.InvokeAsync(() =>
            {
                window!.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "MessageInput").Text = text;
                window.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "SendMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            await SendAsync("测试打断请求");
            while (Volatile.Read(ref calls) < 6) await Task.Delay(50, timeout.Token);
            await Dispatcher.UIThread.InvokeAsync(() => window!.GetVisualDescendants().OfType<Button>().Single(item => Equals(item.Content, "打断回复与朗读")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            releaseLateReply.TrySetResult();
            await lateReplyFinished.Task.WaitAsync(timeout.Token);
            await Task.Delay(200, timeout.Token);
            check(await Dispatcher.UIThread.InvokeAsync(() => !window!.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "ConversationTranscript").Text!.Contains("M5_LATE_REPLY", StringComparison.Ordinal)), "真实 HTTP 请求被打断后迟到回复不进入界面");
            await SendAsync("测试网络失败");
            while (voice.LastError is null) { if (server.IsFaulted) await server; await Task.Delay(50, timeout.Token); }
            check(calls == 7 && !string.IsNullOrWhiteSpace(voice.LastError) && !File.Exists(target), "真实 HTTP 503 显示错误且不执行行动");
            config.Save();
            check(!Directory.EnumerateFiles(GlobalConstants.PluginConfigFolder!, "*.json", SearchOption.AllDirectories).Any(path => File.ReadAllText(path).Contains(fixtureKey, StringComparison.Ordinal)), "会话 API 密钥没有进入配置或对话文件");
        }
        finally
        {
            if (voice is not null) await voice.StopAsync(CancellationToken.None);
            stop.Cancel(); listener.Stop();
            try { await server; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } catch (HttpListenerException) when (stop.IsCancellationRequested) { }
            ai.SessionApiKey = oldKey;
            (config.Data.EnableAiService, config.Data.AiApiUrl, config.Data.AiModel) = original;
            config.Save();
        }
    }

    private static Window[] Windows() => ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.ToArray();
}

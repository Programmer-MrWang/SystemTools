using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using SystemTools.Common.Media;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Models;
using SystemTools.Services;

namespace SystemTools;

public sealed class NativeVoiceService(PortableSpeechService input, PlatformMediaServices media,
    MediaPreferencesStore preferences, MainConfigHandler config, IOpenAiCompatibleService ai,
    AiPromptService prompt, AiChatOperationGate gate, AiConversationStore conversations,
    ClassIslandActionAiService actions, ClassIslandProfileAiService profiles, NativeWindowMaterial material) : IHostedService
{
    private IDisposable? _wake;
    private Window? _window;
    private CancellationTokenSource? _session;
    private ConversationTurns? _turns;
    private Task _listening = Task.CompletedTask;
    public string? LastError { get; private set; }

    public Task StartAsync(CancellationToken token)
    {
        config.Data.PropertyChanged += Changed;
        preferences.Changed += PreferencesChanged;
        ApplyConfig();
        return Task.CompletedTask;
    }
    private void PreferencesChanged(object? sender, EventArgs args) => ApplyConfig();
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainConfigData.AiConversationFloatingWindowStyle)) Dispatcher.UIThread.Post(() => { if (_window is { } window) material.Update(window, config.Data.AiConversationFloatingWindowStyle); });
        if (args.PropertyName is nameof(MainConfigData.EnableVoiceWakeAi) or nameof(MainConfigData.AiWakeWord) or nameof(MainConfigData.EnableAiService)) ApplyConfig();
    }
    public void ApplyConfig()
    {
        _wake?.Dispose(); _wake = null;
        if (!config.Data.EnableVoiceWakeAi || !config.Data.EnableAiService || media.Stopping.IsCancellationRequested) return;
        var status = media.Speech.Probe();
        if (!status.CanExecute) { LastError = status.Reason; return; }
        var word = string.IsNullOrWhiteSpace(config.Data.AiWakeWord) ? "你好ci" : config.Data.AiWakeWord;
        _wake = input.Input.RegisterKeyword(word, 0.8, () => Dispatcher.UIThread.Post(Open));
        LastError = null;
    }
    public Task OpenAsync(bool listen = true) => Dispatcher.UIThread.InvokeAsync(() => Open(listen)).GetTask();

    private void Open() => Open(true);
    private void Open(bool listen)
    {
        if (_window is { } existing) { if (existing.IsVisible) existing.Activate(); return; }
        if (!config.Data.EnableAiService || media.Stopping.IsCancellationRequested) return;
        var session = CancellationTokenSource.CreateLinkedTokenSource(media.Stopping);
        var turns = new ConversationTurns(session.Token);
        _session = session; _turns = turns;
        var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(18) };
        var transcript = new TextBox { Name = "ConversationTranscript", IsReadOnly = true, AcceptsReturn = true, MinHeight = 260, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var status = new TextBlock { Text = listen ? "正在准备语音输入…" : "请输入消息。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var typed = new TextBox { Name = "MessageInput", PlaceholderText = "也可以输入文字" };
        var send = new Button { Name = "SendMessage", Content = "发送" };
        var interrupt = new Button { Content = "打断回复与朗读" };
        var end = new Button { Content = "结束对话" };
        panel.Children.Add(transcript); panel.Children.Add(status); panel.Children.Add(typed);
        panel.Children.Add(send); panel.Children.Add(interrupt); panel.Children.Add(end);
        var window = new Window { Title = "SystemTools 语音对话", Width = 640, Height = 590, Content = new ScrollViewer { Content = panel } };
        _window = window;
        var conversation = conversations.CreateConversation();
        var history = new List<AiChatMessage>();
        var lastSpoken = "";
        void Show(string text) => Dispatcher.UIThread.Post(() => { if (!session.IsCancellationRequested) status.Text = text; });
        async Task<string> Respond(string text, CancellationToken token)
        {
            using var ownership = gate.TryAcquireGeneration(window) ?? throw new InvalidOperationException("另一个 AI 请求正在处理，请稍后重试。");
            var messages = new List<AiChatMessage> { new("system", prompt.LoadSystemPrompt(true)) };
            messages.AddRange(history.TakeLast(40));
            messages.Add(new("user", text));
            var listed = new HashSet<string>(StringComparer.Ordinal);
            var described = new HashSet<string>(StringComparer.Ordinal);
            var settings = new HashSet<string>(StringComparer.Ordinal);
            var tools = actions.Tools.Concat(profiles.Tools).ToArray();
            var approvalDenied = false;
            for (var round = 0; round < 8; round++)
            {
                token.ThrowIfCancellationRequested();
                var answer = await ai.CompleteChatWithToolsAsync(messages, tools, cancellationToken: token).ConfigureAwait(false);
                if (answer.ToolCalls is not { Count: > 0 })
                {
                    token.ThrowIfCancellationRequested();
                    history.Add(new("user", text)); history.Add(new("assistant", answer.Content));
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        conversation.Messages.Add(new AiConversationMessage { Role = "user", Content = text });
                        conversation.Messages.Add(new AiConversationMessage { Role = "assistant", Content = answer.Content });
                        conversations.Save();
                    });
                    return answer.Content;
                }
                messages.Add(new("assistant", answer.Content) { ToolCalls = answer.ToolCalls });
                foreach (var call in answer.ToolCalls)
                {
                    token.ThrowIfCancellationRequested();
                    string result;
                    if (approvalDenied && (call.Name == ClassIslandActionAiService.ExecuteActionsToolName ||
                        call.Name == ClassIslandProfileAiService.PatchProfileToolName))
                        result = "{\"status\":\"denied\",\"message\":\"本轮操作已被用户拒绝。\"}";
                    else if (actions.Tools.Any(tool => tool.Name == call.Name))
                    {
                        result = await actions.ExecuteToolAsync(call, preview => ConfirmAsync(window, preview.Summary,
                            string.Join(Environment.NewLine, preview.Items.Select(item => item.Name + Environment.NewLine + item.SettingsJson)), token),
                            listed, described, settings, token).ConfigureAwait(false);
                        Remember(call.Name, result, listed, described, settings);
                    }
                    else
                        result = await profiles.ExecuteToolAsync(call, preview => ConfirmAsync(window, preview.Summary,
                            string.Join(Environment.NewLine, preview.Operations.Select(item => item.Path + ": " + item.Before + " → " + item.After)), token), token).ConfigureAwait(false);
                    using (var response = JsonDocument.Parse(result))
                        if (response.RootElement.TryGetProperty("status", out var outcome) && outcome.GetString() == "denied") approvalDenied = true;
                    messages.Add(new("tool", result) { ToolCallId = call.Id });
                }
            }
            throw new InvalidOperationException("本轮工具调用过多，请缩小请求范围后重试。");
        }
        async Task Submit(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || session.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() => { if (!session.IsCancellationRequested) transcript.Text += "你：" + text + Environment.NewLine; });
            try
            {
                await turns.SubmitAsync(text, Respond, async (reply, token) =>
                {
                    if (media.Playback is null) return;
                    Volatile.Write(ref lastSpoken, reply);
                    try { await media.Playback.SpeakAsync(reply, token).ConfigureAwait(false); }
                    finally { Volatile.Write(ref lastSpoken, ""); }
                }, reply => Dispatcher.UIThread.Post(() =>
                {
                    if (!session.IsCancellationRequested) transcript.Text += "AI：" + reply + Environment.NewLine;
                })).ConfigureAwait(false);
                Show("等待下一句；可直接说话或点击打断。");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { LastError = error.Message; Show(error.Message); }
        }
        send.Click += async (_, _) => { var text = typed.Text ?? ""; typed.Text = ""; await Submit(text); };
        interrupt.Click += (_, _) => { turns.Interrupt(); Volatile.Write(ref lastSpoken, ""); status.Text = "已打断，等待下一句。"; };
        end.Click += (_, _) => window.Close();
        window.Closed += async (_, _) =>
        {
            session.Cancel();
            try { await _listening.ConfigureAwait(false); await turns.DisposeAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) { LastError = error.Message; }
            finally { session.Dispose(); if (ReferenceEquals(_window, window)) { _window = null; _session = null; _turns = null; } }
        };
        window.Show();
        material.Update(window, config.Data.AiConversationFloatingWindowStyle);
        _listening = listen ? Listen() : Task.CompletedTask;
        async Task Listen()
        {
            try
            {
                await foreach (var segment in input.Input.ListenAsync(session.Token).ConfigureAwait(false))
                {
                    if (ConversationTurns.IsPlaybackEcho(segment.Text, Volatile.Read(ref lastSpoken))) continue;
                    if (!segment.IsFinal && SpeechInputRouter.Normalize(segment.Text).Length >= 2) turns.Interrupt();
                    if (segment.IsFinal) _ = Submit(segment.Text);
                    else Show(segment.Text);
                }
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (Exception error) { LastError = error.Message; Show("语音输入未启动：" + error.Message); }
        }
    }

    private static async Task<bool> ConfirmAsync(Window owner, string summary, string details, CancellationToken token)
    {
        var accepted = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!owner.IsVisible) return Task.FromResult(false);
            var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(20) };
            panel.Children.Add(new TextBlock { Text = summary, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            panel.Children.Add(new Expander { Header = "查看具体更改", Content = new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap } });
            var approve = new Button { Content = "确认执行" };
            var reject = new Button { Content = "取消" };
            panel.Children.Add(approve); panel.Children.Add(reject);
            var dialog = new Window { Title = "确认本次操作", Width = 560, Height = 430, Content = new ScrollViewer { Content = panel } };
            approve.Click += (_, _) => dialog.Close(true);
            reject.Click += (_, _) => dialog.Close(false);
            var cancellation = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
            dialog.Closed += (_, _) => cancellation.Dispose();
            return dialog.ShowDialog<bool>(owner);
        });
        token.ThrowIfCancellationRequested();
        return accepted;
    }

    private static void Remember(string name, string result, HashSet<string> listed, HashSet<string> described, HashSet<string> settings)
    {
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "success") return;
        if (name == "list_classisland_app_settings" && root.TryGetProperty("settings", out var properties))
            foreach (var item in properties.EnumerateArray()) settings.Add(item.GetProperty("propertyName").GetString()!);
        if (!root.TryGetProperty("actions", out var actions)) return;
        foreach (var action in actions.EnumerateArray())
            (name == "describe_classisland_actions" ? described : listed).Add(action.GetProperty("id").GetString()!);
    }

    public async Task StopAsync(CancellationToken token)
    {
        config.Data.PropertyChanged -= Changed;
        preferences.Changed -= PreferencesChanged;
        _wake?.Dispose(); _wake = null;
        _session?.Cancel();
        await Dispatcher.UIThread.InvokeAsync(() => _window?.Close());
        await _listening.ConfigureAwait(false);
        if (_turns is { } turns) await turns.DisposeAsync().ConfigureAwait(false);
    }
}

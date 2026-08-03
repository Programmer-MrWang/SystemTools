using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Assists;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SystemTools.ConfigHandlers;
using SystemTools.Models;
using SystemTools.Shared;
using SystemTools.Views;

namespace SystemTools.Services;

/// <summary>
/// Continuous voice conversation controller. AI replies are broadcast through
/// ClassIsland's configured speech service before the next listening turn starts.
/// </summary>
public sealed class AiVoiceConversationService(
    KeywordSpeechService keywordSpeechService,
    VoskSpeechService speechService,
    IOpenAiCompatibleService aiService,
    AiPromptService promptService,
    AiConversationStore conversationStore,
    AiChatOperationGate operationGate,
    SystemToolsNotificationProvider notificationProvider,
    ClassIslandProfileAiService profileAiService,
    ClassIslandActionAiService actionAiService,
    MainConfigHandler configHandler,
    ClassIslandSettingsService classIslandSettingsService,
    MainWindowAreaService mainWindowAreaService,
    MainWindowTextOcclusionService mainWindowTextOcclusionService,
    IHotkeyService hotkeyService,
    ILogger<AiVoiceConversationService> logger,
    ISpeechService classIslandSpeechService,
    IAudioService audioService) : IDisposable
{
    private const uint EscapeVirtualKey = 0x1B;
    private const string SystemSpeechProviderId = "classisland.speech.system";
    private const double EstimatedSpeechCharactersPerSecond = 3.0;
    private static readonly TimeSpan SilenceDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SpeechPlaybackIdleThreshold = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SpeechPlaybackStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SpeechPlaybackTotalTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EstimatedSpeechStartupOverhead = TimeSpan.FromSeconds(1.5);
    private const double DefaultMainWindowCornerRadius = 8.0;
    private readonly object _syncRoot = new();
    private IDisposable? _wakeRegistration;
    private CancellationTokenSource? _conversationCancellation;
    private RefCounted<AudioPlaybackDevice>.Lease? _audioPlaybackLease;
    private AiVoiceConversationOverlayWindow? _overlay;
    private FAContentDialog? _activeConfirmationDialog;
    private int _conversationRunning;
    private bool _started;
    private bool _disposed;

    public bool IsWakeWordEnabled => _wakeRegistration is not null && configHandler.Data.EnableVoiceWakeAi;
    public string? LastError { get; private set; }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        configHandler.Data.PropertyChanged += OnConfigPropertyChanged;
        ApplyConfig();
    }

    public void ApplyConfig()
    {
        if (_disposed || !_started)
        {
            return;
        }

        var config = configHandler.Data;
        if (!config.EnableVoiceWakeAi)
        {
            StopConversation();
            UnregisterWakeWord();
            LastError = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(config.AiModel))
        {
            LastError = "请先选择 AI 模型。";
            config.EnableVoiceWakeAi = false;
            configHandler.Save();
            UnregisterWakeWord();
            return;
        }

        var dependencyCheck = DependencyPaths.CheckSpeechRecognitionDependencies();
        if (!dependencyCheck.IsAvailable)
        {
            LastError = dependencyCheck.Message;
            config.EnableVoiceWakeAi = false;
            configHandler.Save();
            UnregisterWakeWord();
            logger.LogWarning("语音唤醒 AI 已关闭：{Reason}", LastError);
            return;
        }

        LastError = null;
        UnregisterWakeWord();
        var keyword = string.IsNullOrWhiteSpace(config.AiWakeWord) ? "你好ci" : config.AiWakeWord;
        _wakeRegistration = keywordSpeechService.RegisterWakeWord(keyword, 0.2, OnWakeWordMatched);
        logger.LogInformation("语音唤醒 AI 已启用，唤醒词：{WakeWord}", keyword);
    }

    private void OnConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainConfigData.EnableVoiceWakeAi) or nameof(MainConfigData.AiWakeWord) or
            nameof(MainConfigData.AiModel))
        {
            Dispatcher.UIThread.Post(ApplyConfig);
        }
    }

    private void OnWakeWordMatched(IDisposable keywordSuspension)
        => TryStartConversation(keywordSuspension, allowWhenDisabled: false);

    public bool TryStartDebugConversation()
    {
        if (_disposed || string.IsNullOrWhiteSpace(configHandler.Data.AiModel))
        {
            return false;
        }

        var dependencyCheck = DependencyPaths.CheckSpeechRecognitionDependencies();
        if (!dependencyCheck.IsAvailable)
        {
            LastError = dependencyCheck.Message;
            return false;
        }

        try
        {
            return TryStartConversation(keywordSpeechService.SuspendListening(), allowWhenDisabled: true);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            logger.LogWarning(ex, "无法启动调试语音唤醒 AI");
            return false;
        }
    }

    private bool TryStartConversation(IDisposable keywordSuspension, bool allowWhenDisabled)
    {
        if (_disposed || (!allowWhenDisabled && !configHandler.Data.EnableVoiceWakeAi) ||
            Interlocked.CompareExchange(ref _conversationRunning, 1, 0) != 0)
        {
            keywordSuspension.Dispose();
            return false;
        }

        try
        {
            _ = Task.Run(() => RunConversationAsync(keywordSuspension, allowWhenDisabled));
            return true;
        }
        catch (Exception ex)
        {
            keywordSuspension.Dispose();
            Interlocked.Exchange(ref _conversationRunning, 0);
            logger.LogWarning(ex, "无法调度语音对话任务");
            return false;
        }
    }

    private async Task RunConversationAsync(IDisposable keywordSuspension, bool allowWhenDisabled)
    {
        var cancellation = new CancellationTokenSource();
        lock (_syncRoot)
        {
            if (_disposed)
            {
                keywordSuspension.Dispose();
                cancellation.Dispose();
                Interlocked.Exchange(ref _conversationRunning, 0);
                return;
            }

            _conversationCancellation = cancellation;
            if (!allowWhenDisabled && !configHandler.Data.EnableVoiceWakeAi)
            {
                cancellation.Cancel();
            }
        }

        IDisposable? captureLease = null;
        IDisposable? mainWindowVisibilityLease = null;
        IDisposable? occlusionSuspension = null;
        IDisposable? escapeHotkeyLease = null;
        VoskSpeechService.ConversationSession? speechConversation = null;
        AiVoiceConversationOverlayWindow? overlay = null;
        AiChatSettingsViewModel? chatViewModel = null;
        Control? opacitySource = null;
        EventHandler<AvaloniaPropertyChangedEventArgs>? applicationPropertyChanged = null;
        EventHandler<AvaloniaPropertyChangedEventArgs>? opacityPropertyChanged = null;

        try
        {
            cancellation.Token.ThrowIfCancellationRequested();

            RefCounted<AudioPlaybackDevice>.Lease? audioPlaybackLease = null;
            try
            {
                audioPlaybackLease = await audioService.TryInitializeDefaultPlaybackDeviceSafeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "无法获取语音播放设备租约，将使用估算等待");
            }
            lock (_syncRoot)
            {
                _audioPlaybackLease = audioPlaybackLease;
            }

            var windowInfo = await Dispatcher.UIThread.InvokeAsync(CaptureMainWindowInfo);
            if (windowInfo is null)
            {
                throw new InvalidOperationException("无法读取 ClassIsland 主界面的布局信息。");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                occlusionSuspension = mainWindowTextOcclusionService.Suspend();
                mainWindowVisibilityLease = classIslandSettingsService.HideMainWindow()
                    ?? throw new InvalidOperationException("无法隐藏 ClassIsland 主界面。");
               overlay = new AiVoiceConversationOverlayWindow(
                   windowInfo.Value.Position,
                   windowInfo.Value.Width,
                   windowInfo.Value.Height,
                   windowInfo.Value.IsDark,
                   windowInfo.Value.Opacity,
                   windowInfo.Value.CornerRadius);
               overlay.SetStatus("你好，我是ci，请稍后……");
                overlay.EscapePressed += OverlayOnEscapePressed;
                overlay.Show();
                overlay.Activate();
                _ = overlay.PlayEntranceAsync();
                _overlay = overlay;
                escapeHotkeyLease = TryRegisterEscapeHotkey();
                chatViewModel = new AiChatSettingsViewModel(
                    conversationStore,
                    aiService,
                    promptService,
                    operationGate,
                    speechService,
                    configHandler,
                    notificationProvider,
                    profileAiService,
                    actionAiService,
                    ConfirmProfileModificationAsync,
                    ConfirmActionExecutionAsync,
                    suppressClassIslandNotificationSharing: true,
                    useVoiceWakePrompt: true);

                var appearanceMainWindow = AppBase.Current.MainWindow;
                opacitySource = appearanceMainWindow?.FindControl<Control>("GridRoot");
                applicationPropertyChanged = (_, args) =>
                {
                    if (args.Property?.Name == "ActualThemeVariant" &&
                        appearanceMainWindow is not null &&
                        ReferenceEquals(_overlay, overlay))
                    {
                        overlay.UpdateAppearance(
                            appearanceMainWindow.ActualThemeVariant == ThemeVariant.Dark,
                            GetMainWindowOpacity(appearanceMainWindow));
                    }
                };
                opacityPropertyChanged = (_, args) =>
                {
                    if (args.Property == MainWindowStylesAssist.BackgroundOpacityProperty &&
                        appearanceMainWindow is not null &&
                        ReferenceEquals(_overlay, overlay))
                    {
                        overlay.UpdateAppearance(
                            appearanceMainWindow.ActualThemeVariant == ThemeVariant.Dark,
                            GetMainWindowOpacity(appearanceMainWindow));
                    }
                };
                if (Application.Current is not null)
                {
                    Application.Current.PropertyChanged += applicationPropertyChanged;
                }
                if (opacitySource is not null)
                {
                    opacitySource.PropertyChanged += opacityPropertyChanged;
                }
            });

            await SetOverlayStatusAsync("你好，我是ci，请稍后……", null, cancellation.Token);
            string? modelLoadError = null;
            var modelLoadTask = speechService.TryAcquireConversationAsync(
                message =>
                {
                    modelLoadError = message;
                    logger.LogWarning("语音唤醒 AI 无法加载模型：{Message}", message);
                },
                cancellation.Token);
            speechConversation = await modelLoadTask;
            if (speechConversation is null)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    modelLoadError ?? "无法加载语音识别模型。");
            }

            await SetOverlayStatusAsync("已就绪；请讲……", null, cancellation.Token);

            while (!cancellation.IsCancellationRequested)
            {
                await SetOverlayStatusAsync("正在聆听……", null, cancellation.Token);
                var turn = new CaptureTurn(SilenceDuration);
                await SetOverlayListeningAsync(true);
                try
                {
                    captureLease = await speechConversation.TryStartCaptureAsync(
                        (text, isFinal) =>
                        {
                            turn.OnText(text, isFinal);
                            var recognizedText = turn.GetText();
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (ReferenceEquals(_overlay, overlay))
                                {
                                    overlay?.SetRecognizedText(recognizedText);
                                }
                            });
                        },
                        turn.OnError,
                        turn.OnSpeechActivity,
                        level => Dispatcher.UIThread.Post(() =>
                        {
                            if (ReferenceEquals(_overlay, overlay))
                            {
                                overlay?.SetAudioLevel(level);
                            }
                        }),
                        cancellation.Token);
                    if (captureLease is null)
                    {
                        throw new InvalidOperationException("无法启动语音识别麦克风。");
                    }

                    await turn.WaitForSilenceAsync(cancellation.Token);
                    await speechConversation.StopCaptureAsync();
                    captureLease.Dispose();
                    captureLease = null;
                }
                finally
                {
                    await SetOverlayListeningAsync(false);
                }

                var userText = turn.GetText();
                if (string.IsNullOrWhiteSpace(userText))
                {
                    continue;
                }

                await SetOverlayStatusAsync("正在等待回应……", null, cancellation.Token);
                string? reply;
                try
                {
                    reply = await SendTurnAsync(chatViewModel, userText, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "语音对话 AI 请求失败");
                    reply = null;
                }

                await SetOverlayStatusAsync(
                    "正在回复……",
                    string.IsNullOrWhiteSpace(reply) ? "AI 暂时没有返回内容。" : null,
                    cancellation.Token);
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    await SpeakReplyAsync(reply, cancellation.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            logger.LogInformation("语音对话已结束。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "语音对话异常结束");
            await SetOverlayStatusBestEffortAsync("语音对话已停止", ex.Message);
        }
        finally
        {
            try
            {
                RefCounted<AudioPlaybackDevice>.Lease? audioLease;
                lock (_syncRoot)
                {
                    audioLease = _audioPlaybackLease;
                    _audioPlaybackLease = null;
                }
                audioLease?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "释放语音播放设备租约失败");
            }

            try
            {
                try
                {
                    if (speechConversation is not null)
                    {
                        await speechConversation.StopCaptureAsync();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "停止语音对话采集失败");
                }

                DisposeBestEffort(captureLease, "释放语音采集租约");
                if (speechConversation is not null)
                {
                    try
                    {
                        await speechConversation.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "释放语音对话模型失败");
                    }
                }

                if (chatViewModel is not null)
                {
                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(chatViewModel.Dispose);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "释放语音对话 AI 上下文失败");
                    }
                }

                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DisposeBestEffort(escapeHotkeyLease, "注销 Esc 热键");
                        if (applicationPropertyChanged is not null &&
                            Application.Current is not null)
                        {
                            Application.Current.PropertyChanged -= applicationPropertyChanged;
                        }
                        if (opacityPropertyChanged is not null && opacitySource is not null)
                        {
                            opacitySource.PropertyChanged -= opacityPropertyChanged;
                        }
                        if (overlay is not null)
                        {
                            try
                            {
                                overlay.EscapePressed -= OverlayOnEscapePressed;
                                overlay.CloseFromOwner();
                            }
                            catch (Exception ex)
                            {
                                logger.LogDebug(ex, "关闭语音对话悬浮窗失败");
                            }
                        }

                        if (ReferenceEquals(_overlay, overlay))
                        {
                            _overlay = null;
                        }

                        DisposeBestEffort(occlusionSuspension, "恢复主界面遮挡检测");
                        DisposeBestEffort(mainWindowVisibilityLease, "恢复 ClassIsland 主界面");
                    });
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "清理语音对话窗口状态失败");
                    DisposeBestEffort(escapeHotkeyLease, "注销 Esc 热键");
                    DisposeBestEffort(occlusionSuspension, "恢复主界面遮挡检测");
                    DisposeBestEffort(mainWindowVisibilityLease, "恢复 ClassIsland 主界面");
                }

                DisposeBestEffort(keywordSuspension, "恢复关键词语音识别");
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_conversationCancellation, cancellation))
                    {
                        _conversationCancellation = null;
                        try
                        {
                            classIslandSpeechService.ClearSpeechQueue();
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "清空语音播报队列失败");
                        }
                    }
                }

                cancellation.Dispose();
                Interlocked.Exchange(ref _conversationRunning, 0);
            }
        }
    }

    private async Task<string?> SendTurnAsync(
        AiChatSettingsViewModel? viewModel,
        string userText,
        CancellationToken cancellationToken)
    {
        if (viewModel is null)
        {
            throw new InvalidOperationException("AI 对话上下文尚未初始化。");
        }

        while (true)
        {
            while (operationGate.IsBusy)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
            }

            var start = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var conversation = viewModel.SelectedConversation
                    ?? throw new InvalidOperationException("没有可用的 AI 对话。");
                var messageCount = conversation.Messages.Count;
                viewModel.InputText = userText;
                var generationTask = viewModel.SendAsync();
                var accepted = viewModel.IsGenerating ||
                               conversation.Messages
                                   .Skip(messageCount)
                                   .Any(message =>
                                       message.IsUser &&
                                       string.Equals(
                                           message.Content,
                                           userText,
                                           StringComparison.Ordinal));
                return new VoiceTurnStart(conversation, messageCount, generationTask, accepted);
            });

            if (!start.Accepted)
            {
                await start.GenerationTask;
                var status = await Dispatcher.UIThread.InvokeAsync(() => viewModel.StatusText);
                if (!status.Contains("另一个聊天窗口", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(status)
                            ? "当前无法向 AI 发送语音识别结果。"
                            : status);
                }

                await Task.Delay(100, cancellationToken);
                continue;
            }

            using var cancellationRegistration =
                cancellationToken.Register(viewModel.StopGeneration);
            try
            {
                await start.GenerationTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                viewModel.StopGeneration();
                try
                {
                    await start.GenerationTask;
                }
                catch
                {
                    // The cancellation from this voice session owns the failure.
                }

                throw;
            }

            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var reply = start.Conversation.Messages
                    .Skip(start.MessageCount)
                    .LastOrDefault(message =>
                        message.IsAssistant &&
                        !message.IsStreaming &&
                        !string.IsNullOrWhiteSpace(message.Content))
                    ?.Content
                    .Trim();
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    return reply;
                }

                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(viewModel.StatusText)
                        ? "AI 服务没有返回可播报的内容。"
                        : viewModel.StatusText);
            });
        }
    }

    private async Task SpeakReplyAsync(string reply, CancellationToken cancellationToken)
    {
        try
        {
            var existingPlayers =
                TryGetActivePlaybackComponents()?.ToHashSet() ?? new HashSet<ISoundPlayer>();
            classIslandSpeechService.ClearSpeechQueue();
            var speechText = SystemToolsNotificationProvider.NormalizeAiReply(reply);
            if (string.IsNullOrWhiteSpace(speechText))
            {
                return;
            }

            classIslandSpeechService.EnqueueSpeechQueue(speechText);
            await WaitForSpeechPlaybackAsync(speechText, existingPlayers, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "语音播报失败，继续下一轮聆听");
        }
    }

    private async Task WaitForSpeechPlaybackAsync(
        string text,
        HashSet<ISoundPlayer> existingPlayers,
        CancellationToken cancellationToken)
    {
        var usesSoundFlowPlayback = !string.Equals(
            classIslandSettingsService.GetSelectedSpeechProvider(),
            SystemSpeechProviderId,
            StringComparison.OrdinalIgnoreCase);
        var startUtc = DateTime.UtcNow;
        var estimatedDeadline = startUtc + EstimateSpeechDuration(text);
        var playbackStartDeadline = startUtc + SpeechPlaybackStartTimeout;
        var playbackSeen = false;
        var canObservePlayback = _audioPlaybackLease is not null;
        DateTime? idleSinceUtc = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            var activePlayers = TryGetActivePlaybackComponents();
            if (activePlayers is null)
            {
                idleSinceUtc = null;
            }
            else
            {
                var newPlayers = activePlayers
                    .Where(player => !existingPlayers.Contains(player))
                    .ToArray();
                if (newPlayers.Length > 0)
                {
                    playbackSeen = true;
                    idleSinceUtc = null;
                }
                else if (playbackSeen)
                {
                    idleSinceUtc ??= now;
                    if (now - idleSinceUtc >= SpeechPlaybackIdleThreshold)
                    {
                        return;
                    }
                }
            }

            if (!playbackSeen)
            {
                var fallbackDeadline = canObservePlayback && usesSoundFlowPlayback
                    ? playbackStartDeadline
                    : estimatedDeadline;
                if (now >= fallbackDeadline)
                {
                    return;
                }
            }
            else if (now - startUtc >= SpeechPlaybackTotalTimeout)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private IReadOnlyList<ISoundPlayer>? TryGetActivePlaybackComponents()
    {
        if (_audioPlaybackLease is null)
        {
            return [];
        }

        try
        {
            return _audioPlaybackLease.Value.MasterMixer.Components
                .OfType<ISoundPlayer>()
                .Where(player => player.State == PlaybackState.Playing)
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan EstimateSpeechDuration(string text)
    {
        var seconds = EstimatedSpeechStartupOverhead.TotalSeconds +
                      text.Length / EstimatedSpeechCharactersPerSecond;
        return TimeSpan.FromSeconds(Math.Max(2.0, seconds));
    }

    private Task<bool> ConfirmProfileModificationAsync(ProfileModificationPreview preview)
    {
        var operationText = string.Join(
            Environment.NewLine + Environment.NewLine,
            preview.Operations.Select(operation =>
                operation.Operation switch
                {
                    "add" => $"ADD {operation.Path}\n  新值：{operation.After}",
                    "remove" => $"REMOVE {operation.Path}\n  原值：{operation.Before}",
                    _ => $"REPLACE {operation.Path}\n  原值：{operation.Before}\n  新值：{operation.After}"
                }));
        return ShowToolConfirmationAsync(
            "允许 AI 修改 ClassIsland 档案？",
            $"档案文件：{preview.ProfileFilePath}\n修改说明：{preview.Summary}",
            operationText,
            "AI 可能误解指令；课表、时间表或教师信息的错误修改可能立即影响显示、提醒和自动化。请确认上方路径和值准确后再允许。",
            "允许并保存");
    }

    private Task<bool> ConfirmActionExecutionAsync(ActionExecutionPreview preview)
    {
        var actionText = string.Join(
            Environment.NewLine + Environment.NewLine,
            preview.Items.Select(item =>
                $"{item.Index}. {item.Name}\nID: {item.Id}\n参数: {item.SettingsJson}"));
        return ShowToolConfirmationAsync(
            preview.Items.Count == 1
                ? "允许 AI 执行此行动？"
                : $"允许 AI 执行这 {preview.Items.Count} 项行动？",
            $"执行说明：{preview.Summary}",
            actionText,
            "这些行动可能启动程序、模拟输入、修改文件或系统状态。允许后将按上方顺序立即执行，请确认行动 ID 和参数符合要求。",
            "允许执行");
    }

    private Task<bool> ShowToolConfirmationAsync(
        string title,
        string summary,
        string details,
        string warning,
        string primaryButtonText)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowToolConfirmationOnUiThreadAsync(
                title,
                summary,
                details,
                warning,
                primaryButtonText);
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.SetResult(await ShowToolConfirmationOnUiThreadAsync(
                    title,
                    summary,
                    details,
                    warning,
                    primaryButtonText));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private async Task<bool> ShowToolConfirmationOnUiThreadAsync(
        string title,
        string summary,
        string details,
        string warning,
        string primaryButtonText)
    {
        if (_overlay is not { IsVisible: true } overlay)
        {
            return false;
        }

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 640,
                Children =
                {
                    new TextBlock
                    {
                        Text = summary,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new ScrollViewer
                    {
                        MaxHeight = 300,
                        HorizontalScrollBarVisibility =
                            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility =
                            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = details,
                            FontFamily = new Avalonia.Media.FontFamily("Consolas"),
                            TextWrapping = TextWrapping.NoWrap
                        }
                    },
                    new TextBlock
                    {
                        Text = warning,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };

        CancellationToken cancellationToken;
        lock (_syncRoot)
        {
            if (_conversationCancellation is null)
            {
                return false;
            }

            cancellationToken = _conversationCancellation.Token;
        }

        _activeConfirmationDialog = dialog;
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_activeConfirmationDialog, dialog))
                    {
                        try
                        {
                            dialog.Hide();
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "取消 AI 工具确认对话框失败");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "调度 AI 工具确认对话框取消操作失败");
            }
        });
        try
        {
            // The host window is deliberately hidden during a voice session;
            // keeping the dialog owned by the topmost overlay guarantees that
            // confirmations remain reachable instead of appearing underneath
            // the hidden host window.
            return await dialog.ShowAsync(overlay) == FAContentDialogResult.Primary;
        }
        finally
        {
            if (ReferenceEquals(_activeConfirmationDialog, dialog))
            {
                _activeConfirmationDialog = null;
            }
        }
    }

    private WindowInfo? CaptureMainWindowInfo()
    {
        if (AppBase.Current.MainWindow is not { } mainWindow)
        {
            return null;
        }

        var areas = mainWindowAreaService.GetLayoutAreas();
        if (areas.Count == 0)
        {
            var bounds = mainWindow.Bounds;
            var position = mainWindow.Position;
           return new WindowInfo(
               position,
               Math.Max(1, bounds.Width),
               Math.Max(1, bounds.Height),
               mainWindow.ActualThemeVariant == ThemeVariant.Dark,
               GetMainWindowOpacity(mainWindow),
               GetMainWindowCornerRadius(mainWindow));
        }

        var union = areas.Skip(1).Aggregate(areas[0], Rectangle.Union);
        var scaling = Math.Max(0.1, mainWindow.RenderScaling);
       return new WindowInfo(
           new PixelPoint(union.Left, union.Top),
           union.Width / scaling,
           union.Height / scaling,
           mainWindow.ActualThemeVariant == ThemeVariant.Dark,
           GetMainWindowOpacity(mainWindow),
           GetMainWindowCornerRadius(mainWindow));
    }

   private static double GetMainWindowOpacity(Control mainWindow)
   {
       var gridRoot = mainWindow.FindControl<Control>("GridRoot");
       if (gridRoot is null)
       {
           return 0.5;
       }

       return MainWindowStylesAssist.GetBackgroundOpacity(gridRoot);
   }

   private static double GetMainWindowCornerRadius(Control mainWindow)
   {
       var gridRoot = mainWindow.FindControl<Control>("GridRoot");
       if (gridRoot is null)
       {
           return DefaultMainWindowCornerRadius;
       }

       return MainWindowStylesAssist.GetCornerRadius(gridRoot);
   }

    private async Task SetOverlayStatusAsync(string status, string? detail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() => _overlay?.SetStatus(status, detail));
    }

    private async Task SetOverlayStatusBestEffortAsync(string status, string? detail)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => _overlay?.SetStatus(status, detail));
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Error reporting must never prevent cleanup.
        }
    }

    private async Task SetOverlayListeningAsync(bool isListening)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => _overlay?.SetListening(isListening));
        }
        catch
        {
            // The overlay may have been closed while a capture lease unwinds.
        }
    }

    private void OverlayOnEscapePressed(object? sender, EventArgs e) => StopConversation();

    private void DisposeBestEffort(IDisposable? disposable, string operation)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "{Operation}失败", operation);
        }
    }

    private IDisposable? TryRegisterEscapeHotkey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var hotkeyId = hotkeyService.RegisterHotkey(0, EscapeVirtualKey);
            EventHandler<HotkeyEventArgs> handler = (_, args) =>
            {
                if (args.HotkeyId == hotkeyId)
                {
                    StopConversation();
                }
            };
            hotkeyService.HotkeyPressed += handler;
            return new CallbackDisposable(() =>
            {
                hotkeyService.HotkeyPressed -= handler;
                hotkeyService.UnregisterHotkey(hotkeyId);
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "无法注册全局 Esc，语音对话仍可在悬浮窗获得焦点时退出");
            return null;
        }
    }

    public void StopConversation()
    {
        CancellationTokenSource? cancellation;
        lock (_syncRoot)
        {
            cancellation = _conversationCancellation;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "取消语音对话失败");
        }
    }

    private void UnregisterWakeWord()
    {
        _wakeRegistration?.Dispose();
        _wakeRegistration = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        configHandler.Data.PropertyChanged -= OnConfigPropertyChanged;
        StopConversation();
        UnregisterWakeWord();
    }

   private readonly record struct WindowInfo(
       PixelPoint Position,
       double Width,
       double Height,
       bool IsDark,
       double Opacity,
       double CornerRadius);

    private readonly record struct VoiceTurnStart(
        AiConversation Conversation,
        int MessageCount,
        Task GenerationTask,
        bool Accepted);

    private sealed class CaptureTurn(TimeSpan silenceDuration)
    {
        private readonly object _lock = new();
        private string _committed = string.Empty;
        private string _partial = string.Empty;
        private string? _error;
        private DateTime _lastActivityUtc;
        private bool _hasActivity;

        public void OnSpeechActivity()
        {
            lock (_lock)
            {
                _hasActivity = true;
                _lastActivityUtc = DateTime.UtcNow;
            }
        }

        public void OnError(string message)
        {
            lock (_lock)
            {
                _error = string.IsNullOrWhiteSpace(message)
                    ? "语音识别发生未知错误。"
                    : message;
            }
        }

        public void OnText(string text, bool isFinal)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_lock)
            {
                if (isFinal)
                {
                    _committed = AppendText(_committed, text);
                    _partial = string.Empty;
                }
                else
                {
                    _partial = text.Trim();
                }

                _hasActivity = true;
                _lastActivityUtc = DateTime.UtcNow;
            }
        }

        public async Task WaitForSilenceAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                lock (_lock)
                {
                    if (_error is not null)
                    {
                        throw new InvalidOperationException(_error);
                    }

                    if (_hasActivity && DateTime.UtcNow - _lastActivityUtc >= silenceDuration)
                    {
                        return;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }
        }

        public string GetText()
        {
            lock (_lock)
            {
                return AppendText(_committed, _partial).Trim();
            }
        }

        private static string AppendText(string existing, string next)
        {
            if (existing.Length == 0) return next.Trim();
            if (next.Length == 0) return existing;
            var needsSpace = IsAsciiWordCharacter(existing[^1]) &&
                             IsAsciiWordCharacter(next[0]);
            return existing + (needsSpace ? " " : string.Empty) + next.Trim();
        }

        private static bool IsAsciiWordCharacter(char character) =>
            character <= sbyte.MaxValue && char.IsLetterOrDigit(character);
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose()
        {
            Interlocked.Exchange(ref _callback, null)?.Invoke();
        }
    }
}

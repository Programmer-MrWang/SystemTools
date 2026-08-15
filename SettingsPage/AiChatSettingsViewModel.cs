using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemTools.ConfigHandlers;
using SystemTools.Models;
using SystemTools.Services;

namespace SystemTools;

public partial class AiChatSettingsViewModel : ObservableObject, IDisposable
{
    private const string VoiceInputModelLoadingMessage =
        "\u6B63\u5728\u52A0\u8F7D\u8BED\u97F3\u8F93\u5165\u6A21\u578B\u2026\u2026";
    private readonly AiConversationStore _store;
    private readonly IOpenAiCompatibleService _aiService;
    private readonly AiPromptService _promptService;
    private readonly AiChatOperationGate _operationGate;
    private readonly VoskSpeechService _speechService;
    private readonly MainConfigHandler _configHandler;
    private readonly SystemToolsNotificationProvider _notificationProvider;
    private readonly ClassIslandProfileAiService _profileAiService;
    private readonly ClassIslandActionAiService _actionAiService;
    private readonly Func<ProfileModificationPreview, Task<bool>> _confirmProfileModificationAsync;
    private readonly Func<ActionExecutionPreview, Task<bool>> _confirmActionExecutionAsync;
    private readonly bool _suppressClassIslandNotificationSharing;
    private readonly bool _useVoiceWakePrompt;
    private readonly bool _useTransientConversation;
    private readonly Dictionary<Guid, ComposerDraft> _composerDrafts = [];
    private CancellationTokenSource? _generationCancellation;
    private Task _generationTask = Task.CompletedTask;
    private AiConversation? _generatingConversation;
    private IDisposable? _attachmentUpdateLease;
    private IDisposable? _voiceInputLease;
    private CancellationTokenSource? _voiceInputStartCancellation;
    private string _voiceInputPrefix = string.Empty;
    private string _voiceInputCommittedText = string.Empty;
    private bool _isVoiceInputStarting;
    private bool _isDisposed;

    [ObservableProperty] private AiConversation? _selectedConversation;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isHistoryOpen = true;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _isUpdatingAttachments;
    [ObservableProperty] private bool _isVoiceInputActive;
    [ObservableProperty] private string _statusText = string.Empty;

    public ObservableCollection<AiAttachment> PendingAttachments { get; } = [];

    public AiChatSettingsViewModel(
        AiConversationStore store,
        IOpenAiCompatibleService aiService,
        AiPromptService promptService,
        AiChatOperationGate operationGate,
        VoskSpeechService speechService,
        MainConfigHandler configHandler,
        SystemToolsNotificationProvider notificationProvider,
        ClassIslandProfileAiService profileAiService,
        ClassIslandActionAiService actionAiService,
        Func<ProfileModificationPreview, Task<bool>> confirmProfileModificationAsync,
        Func<ActionExecutionPreview, Task<bool>> confirmActionExecutionAsync,
        bool suppressClassIslandNotificationSharing = false,
        bool useVoiceWakePrompt = false,
        bool useTransientConversation = false)
    {
        _store = store;
        _aiService = aiService;
        _promptService = promptService;
        _operationGate = operationGate;
        _speechService = speechService;
        _configHandler = configHandler;
        _notificationProvider = notificationProvider;
        _profileAiService = profileAiService;
        _actionAiService = actionAiService;
        _confirmProfileModificationAsync = confirmProfileModificationAsync;
        _confirmActionExecutionAsync = confirmActionExecutionAsync;
        _suppressClassIslandNotificationSharing = suppressClassIslandNotificationSharing;
        _useVoiceWakePrompt = useVoiceWakePrompt;
        _useTransientConversation = useTransientConversation;
        PendingAttachments.CollectionChanged += OnPendingAttachmentsChanged;
        Conversations.CollectionChanged += OnConversationsCollectionChanged;
        _operationGate.StateChanged += OnOperationGateStateChanged;
        _speechService.DictationStateChanged += OnDictationStateChanged;

        var selected = useTransientConversation
            ? new AiConversation()
            : store.Conversations.FirstOrDefault(x => x.Id == store.ActiveConversationId)
              ?? store.Conversations.FirstOrDefault()
              ?? store.CreateConversation();
        SelectedConversation = selected;

        if (!string.IsNullOrWhiteSpace(store.LastLoadError))
        {
            StatusText = $"部分历史记录无法加载：{store.LastLoadError}";
        }
    }

    public ObservableCollection<AiConversation> Conversations => _store.Conversations;

    public string CurrentModelName => string.IsNullOrWhiteSpace(_configHandler.Data.AiModel)
        ? "未选择模型"
        : _configHandler.Data.AiModel;

    public string InputPlaceholder => string.IsNullOrWhiteSpace(_configHandler.Data.AiModel)
        ? "请先在“更多功能选项”中获取并选择模型"
        : "随心输入……";

    public bool CanSend => !IsGenerating &&
                           !IsUpdatingAttachments &&
                           !_operationGate.IsBusy &&
                           SelectedConversation is not null &&
                           (!string.IsNullOrWhiteSpace(InputText) || PendingAttachments.Count > 0);

    public bool IsAnyGenerationActive => _operationGate.IsGenerationActive;

    public bool IsNoGenerationActive => !IsAnyGenerationActive;

    public bool CanChangeConversation => !_operationGate.IsBusy;

    public bool CanModifyAttachments => !IsGenerating &&
                                        !IsUpdatingAttachments &&
                                        !_operationGate.IsBusy;

    public bool CanToggleVoiceInput => IsVoiceInputActive ||
                                       _isVoiceInputStarting ||
                                       (!IsGenerating &&
                                        !IsUpdatingAttachments &&
                                        !_operationGate.IsBusy &&
                                        SelectedConversation is not null &&
                                        !_speechService.IsDictationActive);

    public string VoiceInputToolTip => IsVoiceInputActive
        ? "停止语音输入"
        : _isVoiceInputStarting ? VoiceInputModelLoadingMessage : "语音输入";

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public bool HasMessages => SelectedConversation?.Messages.Count > 0;

    public bool HasPendingAttachments => PendingAttachments.Count > 0;

    public long PendingAttachmentBytes => PendingAttachments.Sum(x => x.Size);

    public bool IsClassIslandNotificationSharingEnabled
    {
        get => _configHandler.Data.ShareAiRepliesWithClassIslandNotifications;
        set
        {
            if (value == _configHandler.Data.ShareAiRepliesWithClassIslandNotifications)
            {
                return;
            }

            _configHandler.Data.ShareAiRepliesWithClassIslandNotifications = value;
            OnPropertyChanged();
        }
    }

    public event EventHandler? ConversationContentChanged;

    public async Task ToggleVoiceInputAsync()
    {
        ThrowIfDisposed();
        if (IsVoiceInputActive || _isVoiceInputStarting)
        {
            StopVoiceInput();
            return;
        }

        if (!CanToggleVoiceInput)
        {
            StatusText = _speechService.IsDictationActive
                ? "另一个 AI 对话窗口正在使用语音输入"
                : "当前无法启用语音输入";
            return;
        }

        _voiceInputPrefix = InputText;
        _voiceInputCommittedText = string.Empty;
        var startCancellation = new CancellationTokenSource();
        _voiceInputStartCancellation = startCancellation;
        _isVoiceInputStarting = true;
        IsVoiceInputActive = false;
        StatusText = VoiceInputModelLoadingMessage;
        OnPropertyChanged(nameof(CanToggleVoiceInput));
        OnPropertyChanged(nameof(VoiceInputToolTip));

        try
        {
            var lease = await _speechService.TryStartDictationAsync(
                OnVoiceInputText,
                OnVoiceInputError,
                BuildVoiceInputContext(),
                startCancellation.Token);
            if (_isDisposed || startCancellation.IsCancellationRequested)
            {
                lease?.Dispose();
                return;
            }

            if (lease == null)
            {
                IsVoiceInputActive = false;
                if (string.IsNullOrWhiteSpace(StatusText) ||
                    StatusText == VoiceInputModelLoadingMessage)
                {
                    StatusText = "无法启用语音输入；请确认依赖目录中存在认证模型和最新的 VoskWorker 文件夹，并允许麦克风访问";
                }
                return;
            }

            _voiceInputLease = lease;
            IsVoiceInputActive = true;
            StatusText = string.Empty;
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _voiceInputStartCancellation,
                null,
                startCancellation);
            startCancellation.Dispose();
            _isVoiceInputStarting = false;
            OnPropertyChanged(nameof(CanToggleVoiceInput));
            OnPropertyChanged(nameof(VoiceInputToolTip));
        }
    }

    public void StopVoiceInput()
    {
        var startCancellation = Interlocked.Exchange(ref _voiceInputStartCancellation, null);
        startCancellation?.Cancel();
        startCancellation?.Dispose();
        IsVoiceInputActive = false;
        Interlocked.Exchange(ref _voiceInputLease, null)?.Dispose();
        _voiceInputPrefix = string.Empty;
        _voiceInputCommittedText = string.Empty;
        if (StatusText == VoiceInputModelLoadingMessage)
        {
            StatusText = string.Empty;
        }
    }

    public AiConversation CreateNewConversation()
    {
        ThrowIfDisposed();
        if (_operationGate.IsBusy)
        {
            StatusText = "另一个聊天窗口正在处理附件或生成回复，请稍后再新建对话";
            return SelectedConversation ?? throw new InvalidOperationException(
                "AI 聊天正忙且当前没有可用对话。");
        }

        var conversation = _store.CreateConversation();
        SelectedConversation = conversation;
        StatusText = string.Empty;
        return conversation;
    }

    public async Task DeleteConversationAsync(AiConversation conversation)
    {
        ThrowIfDisposed();

        if (_operationGate.IsBusy && !ReferenceEquals(conversation, _generatingConversation))
        {
            StatusText = "另一个聊天窗口正在处理附件或生成回复，请稍后再删除对话";
            return;
        }

        if (ReferenceEquals(conversation, _generatingConversation))
        {
            StopGeneration();
            await _generationTask;
        }

        if (!_store.DeleteConversation(conversation))
        {
            return;
        }

        SelectedConversation = _store.Conversations.FirstOrDefault(x => x.Id == _store.ActiveConversationId)
                               ?? _store.Conversations.FirstOrDefault()
                               ?? _store.CreateConversation();
    }

    public void SaveConversationTitle()
    {
        if (SelectedConversation is null || _operationGate.IsBusy)
        {
            return;
        }

        SelectedConversation.Title = SelectedConversation.Title;
        _store.Touch(SelectedConversation);
        TrySaveStore();
    }

    public async Task SendAsync()
    {
        ThrowIfDisposed();
        if (!CanSend || SelectedConversation is null)
        {
            return;
        }

        if (IsVoiceInputActive || _isVoiceInputStarting)
        {
            StopVoiceInput();
        }

        var generationLease = _operationGate.TryAcquireGeneration(this);
        if (generationLease is null)
        {
            StatusText = "另一个聊天窗口正在处理附件或生成回复，请稍后再试";
            return;
        }

        using (generationLease)
        {
            var conversation = SelectedConversation;
            var userText = InputText.Trim();
            var attachments = PendingAttachments.ToList();
            LocalActionRoute? localRoute = null;
            if (attachments.Count == 0)
            {
                try
                {
                    localRoute = _actionAiService.ResolveLocalRoute(userText);
                }
                catch
                {
                    localRoute = null;
                }
            }

            var executeLocally = localRoute?.CanExecuteDirectly == true;
            var systemPrompt = string.Empty;
            if (!executeLocally && string.IsNullOrWhiteSpace(_configHandler.Data.AiModel))
            {
                StatusText = "请先在“更多功能选项”中获取并选择模型。";
                return;
            }
            if (!executeLocally && !TryLoadSystemPrompt(out systemPrompt))
            {
                return;
            }

            InputText = string.Empty;
            StatusText = string.Empty;

            var isFirstUserMessage = conversation.Messages.All(x => !x.IsUser);
            PendingAttachments.Clear();
            var userMessage = new AiConversationMessage
            {
                Role = "user",
                Content = userText,
                Attachments = new ObservableCollection<AiAttachment>(attachments)
            };
            userMessage.InitializeRuntimeState();
            conversation.Messages.Add(userMessage);

            if (isFirstUserMessage)
            {
                conversation.Title = CreateConversationTitle(
                    string.IsNullOrWhiteSpace(userText) ? attachments[0].FileName : userText);
            }

            _store.Touch(conversation);
            TrySaveStore();

            if (executeLocally)
            {
                await ExecuteLocalRouteForConversationAsync(conversation, localRoute!);
            }
            else
            {
                await GenerateResponseForConversationAsync(conversation, systemPrompt, localRoute);
            }
        }
    }

    public void BeginEditUserMessage(AiConversationMessage message)
    {
        ThrowIfDisposed();
        if (IsGenerating || _operationGate.IsBusy || !message.IsUser ||
            SelectedConversation?.Messages.Contains(message) != true)
        {
            return;
        }

        foreach (var item in SelectedConversation.Messages.Where(x => x.IsEditing))
        {
            item.IsEditing = false;
        }

        message.DraftContent = message.Content;
        message.IsEditing = true;
        StatusText = string.Empty;
    }

    public void CancelEditUserMessage(AiConversationMessage message)
    {
        message.DraftContent = message.Content;
        message.IsEditing = false;
    }

    public async Task CommitEditedUserMessageAsync(AiConversationMessage message)
    {
        ThrowIfDisposed();
        var conversation = SelectedConversation;
        if (IsGenerating || conversation is null || !message.IsUser)
        {
            return;
        }

        var messageIndex = conversation.Messages.IndexOf(message);
        var editedText = message.DraftContent.Trim();
        if (messageIndex < 0 ||
            (string.IsNullOrWhiteSpace(editedText) && message.Attachments.Count == 0))
        {
            StatusText = "消息内容不能为空";
            return;
        }

        if (!TryLoadSystemPrompt(out var systemPrompt))
        {
            return;
        }

        var generationLease = _operationGate.TryAcquireGeneration(this);
        if (generationLease is null)
        {
            StatusText = "另一个聊天窗口正在处理附件或生成回复，请稍后再试";
            return;
        }

        using (generationLease)
        {
            message.Content = editedText;
            message.DraftContent = editedText;
            message.IsEditing = false;
            RemoveMessagesAfter(conversation, messageIndex);

            if (conversation.Messages.Take(messageIndex).All(x => !x.IsUser))
            {
                conversation.Title = CreateConversationTitle(
                    string.IsNullOrWhiteSpace(editedText)
                        ? message.Attachments[0].FileName
                        : editedText);
            }

            _store.Touch(conversation);
            TrySaveStore();
            StatusText = string.Empty;
            await GenerateResponseForConversationAsync(conversation, systemPrompt);
        }
    }

    public async Task RetryAssistantMessageAsync(AiConversationMessage assistantMessage)
    {
        ThrowIfDisposed();
        var conversation = SelectedConversation;
        if (IsGenerating || conversation is null || !assistantMessage.IsAssistant)
        {
            return;
        }

        var assistantIndex = conversation.Messages.IndexOf(assistantMessage);
        var userMessageIndex = FindPreviousUserMessageIndex(conversation, assistantIndex);
        if (assistantIndex < 0 || userMessageIndex < 0)
        {
            return;
        }

        if (!TryLoadSystemPrompt(out var systemPrompt))
        {
            return;
        }

        var generationLease = _operationGate.TryAcquireGeneration(this);
        if (generationLease is null)
        {
            StatusText = "另一个聊天窗口正在处理附件或生成回复，请稍后再试";
            return;
        }

        using (generationLease)
        {
            RemoveMessagesAfter(conversation, userMessageIndex);
            _store.Touch(conversation);
            TrySaveStore();
            StatusText = string.Empty;
            await GenerateResponseForConversationAsync(conversation, systemPrompt);
        }
    }

    public void ReportError(string message)
    {
        StatusText = message;
    }

    public void AddPendingAttachments(IEnumerable<AiAttachment> attachments)
    {
        var accepted = attachments.ToList();
        if (_isDisposed || _attachmentUpdateLease is null)
        {
            foreach (var attachment in accepted)
            {
                attachment.Dispose();
            }

            return;
        }

        foreach (var attachment in accepted)
        {
            PendingAttachments.Add(attachment);
        }
    }

    public void RemovePendingAttachment(AiAttachment attachment)
    {
        ThrowIfDisposed();
        if (!CanModifyAttachments)
        {
            return;
        }

        using var attachmentLease = _operationGate.TryAcquireAttachmentUpdate(this);
        if (attachmentLease is null || !PendingAttachments.Remove(attachment))
        {
            return;
        }

        attachment.Dispose();
    }

    private async Task ExecuteLocalRouteForConversationAsync(
        AiConversation conversation,
        LocalActionRoute route)
    {
        var assistantMessage = new AiConversationMessage
        {
            Role = "assistant",
            IsStreaming = true,
            ActivityText = "正在准备行动..."
        };
        conversation.Messages.Add(assistantMessage);

        _generationCancellation?.Dispose();
        _generationCancellation = new CancellationTokenSource();
        _generatingConversation = conversation;
        IsGenerating = true;
        _generationTask = ExecuteLocalRouteAsync(
            conversation,
            assistantMessage,
            route,
            _generationCancellation.Token);
        await _generationTask;
    }

    private async Task ExecuteLocalRouteAsync(
        AiConversation conversation,
        AiConversationMessage assistantMessage,
        LocalActionRoute route,
        CancellationToken cancellationToken)
    {
        var reply = string.Empty;
        var hasCompleted = false;
        try
        {
            var result = await _actionAiService.ExecuteLocalRouteAsync(
                route,
                _confirmActionExecutionAsync,
                cancellationToken);
            var status = TryGetToolStatus(result);
            reply = status switch
            {
                "completed" => $"已执行：{route.ActionName}。",
                "partially_completed" => $"已执行“{route.ActionName}”，但行动未完全完成。",
                "denied" => $"已取消执行：{route.ActionName}。",
                _ => $"未能执行：{route.ActionName}。"
            };
            hasCompleted = status is "completed" or "partially_completed";
            await UpdateAssistantContentAsync(assistantMessage, reply);
            if (status is "denied")
            {
                StatusText = "行动执行已取消。";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "已停止行动执行。";
        }
        catch (Exception ex)
        {
            reply = $"行动执行失败：{ex.Message}";
            await UpdateAssistantContentAsync(assistantMessage, reply);
            StatusText = reply;
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                assistantMessage.ActivityText = string.Empty;
                assistantMessage.IsStreaming = false;
                if (string.IsNullOrWhiteSpace(assistantMessage.Content))
                {
                    conversation.Messages.Remove(assistantMessage);
                }
            });

            _store.Touch(conversation);
            TrySaveStore();
            _generatingConversation = null;
            IsGenerating = false;
            _generationCancellation?.Dispose();
            _generationCancellation = null;

            if (hasCompleted && IsClassIslandNotificationSharingEnabled &&
                !_suppressClassIslandNotificationSharing && reply.Length > 0)
            {
                try
                {
                    await RunOnUiThreadAsync(
                        () => _notificationProvider.ShowAiReplyNotification(reply));
                }
                catch (Exception ex)
                {
                    StatusText = $"行动已执行，但通知发送失败：{ex.Message}";
                }
            }
        }
    }

    private async Task GenerateResponseForConversationAsync(
        AiConversation conversation,
        string systemPrompt,
        LocalActionRoute? localRoute = null)
    {
        AiChatMessage[] requestMessages;
        try
        {
            var systemMessages = new List<AiChatMessage>
            {
                new("system", systemPrompt)
            };
            if (localRoute is not null)
            {
                systemMessages.Add(new AiChatMessage("system", localRoute.ContextMessage));
            }

            requestMessages = systemMessages
                .Concat(conversation.Messages
                    .Where(x => !string.IsNullOrWhiteSpace(x.Content) || x.Attachments.Count > 0)
                    .Select(CreateRequestMessage))
                .ToArray();
        }
        catch (Exception ex)
        {
            StatusText = $"无法读取历史附件：{ex.Message}";
            return;
        }

        var assistantMessage = new AiConversationMessage
        {
            Role = "assistant",
            IsStreaming = true,
            ActivityText = "正在理解请求..."
        };
        conversation.Messages.Add(assistantMessage);

        _generationCancellation?.Dispose();
        _generationCancellation = new CancellationTokenSource();
        _generatingConversation = conversation;
        IsGenerating = true;

        _generationTask = GenerateResponseAsync(
            conversation,
            assistantMessage,
            requestMessages,
            localRoute,
            _generationCancellation.Token);
        await _generationTask;
    }

    public void StopGeneration()
    {
        _generationCancellation?.Cancel();
    }

    public Task WaitForGenerationAsync()
    {
        return _generationTask;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _generationCancellation?.Cancel();
        StopVoiceInput();
        _operationGate.StateChanged -= OnOperationGateStateChanged;
        _speechService.DictationStateChanged -= OnDictationStateChanged;
        IsUpdatingAttachments = false;
        Interlocked.Exchange(ref _attachmentUpdateLease, null)?.Dispose();
        foreach (var attachment in PendingAttachments)
        {
            attachment.Dispose();
        }
        PendingAttachments.Clear();
        foreach (var draft in _composerDrafts.Values)
        {
            DisposeAttachments(draft.Attachments);
        }
        _composerDrafts.Clear();
        PendingAttachments.CollectionChanged -= OnPendingAttachmentsChanged;
        Conversations.CollectionChanged -= OnConversationsCollectionChanged;
        DetachConversation(SelectedConversation);
    }

    partial void OnSelectedConversationChanged(AiConversation? oldValue, AiConversation? newValue)
    {
        if (IsVoiceInputActive || _isVoiceInputStarting)
        {
            StopVoiceInput();
        }
        StoreComposerDraft(oldValue);
        DetachConversation(oldValue);
        AttachConversation(newValue);
        RestoreComposerDraft(newValue);
        TrySetActiveConversation(newValue);
        OnPropertyChanged(nameof(HasMessages));
        ConversationContentChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        if (value && (IsVoiceInputActive || _isVoiceInputStarting))
        {
            StopVoiceInput();
        }
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanModifyAttachments));
        OnPropertyChanged(nameof(CanToggleVoiceInput));
    }

    partial void OnIsUpdatingAttachmentsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanModifyAttachments));
        OnPropertyChanged(nameof(CanToggleVoiceInput));
    }

    partial void OnIsVoiceInputActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleVoiceInput));
        OnPropertyChanged(nameof(VoiceInputToolTip));
    }

    public bool TryBeginAttachmentUpdate()
    {
        ThrowIfDisposed();
        if (!CanModifyAttachments)
        {
            return false;
        }

        var lease = _operationGate.TryAcquireAttachmentUpdate(this);
        if (lease is null)
        {
            return false;
        }

        _attachmentUpdateLease = lease;
        IsUpdatingAttachments = true;
        return true;
    }

    public void EndAttachmentUpdate()
    {
        if (_isDisposed)
        {
            return;
        }

        IsUpdatingAttachments = false;
        Interlocked.Exchange(ref _attachmentUpdateLease, null)?.Dispose();
    }

    private void OnOperationGateStateChanged(object? sender, EventArgs e)
    {
        void NotifyBindings()
        {
            if (_isDisposed)
            {
                return;
            }

            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanModifyAttachments));
            OnPropertyChanged(nameof(IsAnyGenerationActive));
            OnPropertyChanged(nameof(IsNoGenerationActive));
            OnPropertyChanged(nameof(CanChangeConversation));
            OnPropertyChanged(nameof(CanToggleVoiceInput));
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            NotifyBindings();
        }
        else
        {
            Dispatcher.UIThread.Post(NotifyBindings);
        }
    }

    private void OnDictationStateChanged(object? sender, EventArgs e)
    {
        void NotifyBinding()
        {
            if (!_isDisposed)
            {
                OnPropertyChanged(nameof(CanToggleVoiceInput));
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            NotifyBinding();
        }
        else
        {
            Dispatcher.UIThread.Post(NotifyBinding);
        }
    }

    private void OnVoiceInputText(string text, bool isFinal)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed || !IsVoiceInputActive)
            {
                return;
            }

            if (isFinal)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _voiceInputCommittedText = AppendRecognizedText(_voiceInputCommittedText, text);
                }
                InputText = _voiceInputPrefix + _voiceInputCommittedText;
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                InputText = _voiceInputPrefix + AppendRecognizedText(_voiceInputCommittedText, text);
            }
        });
    }

    private string BuildVoiceInputContext()
    {
        var context = InputText.Trim();
        const int maximumLength = 120;
        return context.Length <= maximumLength ? context : context[^maximumLength..];
    }

    private static string AppendRecognizedText(string existingText, string recognizedText)
    {
        if (existingText.Length == 0 || recognizedText.Length == 0)
        {
            return existingText + recognizedText;
        }

        var needsSpace = char.IsLetterOrDigit(existingText[^1]) &&
                         existingText[^1] <= sbyte.MaxValue &&
                         char.IsLetterOrDigit(recognizedText[0]) &&
                         recognizedText[0] <= sbyte.MaxValue;
        return needsSpace
            ? $"{existingText} {recognizedText}"
            : existingText + recognizedText;
    }

    private void OnVoiceInputError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            IsVoiceInputActive = false;
            Interlocked.Exchange(ref _voiceInputLease, null)?.Dispose();
            StatusText = message;
        });
    }

    private void OnPendingAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasPendingAttachments));
        OnPropertyChanged(nameof(PendingAttachmentBytes));
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatus));
    }

    private async Task GenerateResponseAsync(
        AiConversation conversation,
        AiConversationMessage assistantMessage,
        AiChatMessage[] requestMessages,
        LocalActionRoute? localRoute,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder();
        var streamedContent = new StringBuilder();
        var generationCompleted = false;
        var profileWasModified = false;
        var profileStateIsUncertain = false;
        var profileWriteWasRolledBack = false;
        string? blockedWriteStatus = null;
        var actionExecutionWasAuthorized = false;
        var actionExecutionCompleted = false;
        var actionExecutionWasDenied = false;
        var listedActionIds = new HashSet<string>(StringComparer.Ordinal);
        var describedActionIds = new HashSet<string>(StringComparer.Ordinal);
        var listedAppSettingNames = new HashSet<string>(StringComparer.Ordinal);
        if (localRoute is not null)
        {
            listedActionIds.Add(localRoute.ActionId);
            describedActionIds.Add(localRoute.ActionId);
            listedAppSettingNames.UnionWith(localRoute.AppSettingNames);
        }

        try
        {
            var agentMessages = requestMessages.ToList();
            const int maximumToolRounds = 8;
            const int maximumToolCallsPerRound = 8;
            var tools = _profileAiService.Tools
                .Concat(_actionAiService.GetToolsForRoute(localRoute))
                .ToArray();

            for (var round = 0; round < maximumToolRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                streamedContent.Clear();
                var renderTimer = Stopwatch.StartNew();
                var hasRenderedStreamedContent = false;
                AiChatCompletionResult? result = null;

                await foreach (var update in _aiService.StreamChatCompletionWithToolsAsync(
                                   agentMessages,
                                   tools,
                                   cancellationToken: cancellationToken))
                {
                    if (update.Completion is not null)
                    {
                        result = update.Completion;
                    }

                    if (string.IsNullOrEmpty(update.ContentDelta))
                    {
                        continue;
                    }

                    streamedContent.Append(update.ContentDelta);
                    if (!hasRenderedStreamedContent || renderTimer.ElapsedMilliseconds >= 40)
                    {
                        await UpdateAssistantStreamingContentAsync(
                            assistantMessage,
                            streamedContent.ToString());
                        hasRenderedStreamedContent = true;
                        renderTimer.Restart();
                    }
                }

                if (result is null)
                {
                    throw new InvalidOperationException("AI 流式响应没有返回完成信息。");
                }

                if (streamedContent.Length > 0)
                {
                    await UpdateAssistantStreamingContentAsync(
                        assistantMessage,
                        streamedContent.ToString());
                }

                var toolCalls = result.ToolCalls ?? [];

                if (toolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(result.Content))
                    {
                        throw new InvalidOperationException("AI 服务没有返回最终回复。");
                    }

                    await UpdateAssistantActivityAsync(assistantMessage, string.Empty);
                    content.Append(result.Content);
                    await UpdateAssistantContentAsync(assistantMessage, content.ToString());
                    generationCompleted = true;
                    break;
                }

                if (toolCalls.Count > maximumToolCallsPerRound)
                {
                    throw new InvalidOperationException(
                        $"AI 一次请求了 {toolCalls.Count} 个工具调用，超过安全上限 {maximumToolCallsPerRound}。");
                }

                if (toolCalls.Count(toolCall =>
                        toolCall.Name == ClassIslandActionAiService.ExecuteActionsToolName) > 1)
                {
                    throw new InvalidOperationException(
                        "AI 在同一轮拆分了多个行动执行批次，已在显示审批前拒绝。请将同一请求的全部行动合并为一个批次。");
                }

                await UpdateAssistantContentAsync(assistantMessage, content.ToString());
                streamedContent.Clear();

                var listedBeforeRound = new HashSet<string>(listedActionIds, StringComparer.Ordinal);
                var describedBeforeRound = new HashSet<string>(describedActionIds, StringComparer.Ordinal);
                var listedAppSettingsBeforeRound = new HashSet<string>(
                    listedAppSettingNames,
                    StringComparer.Ordinal);

                agentMessages.Add(new AiChatMessage(
                    "assistant",
                    string.IsNullOrWhiteSpace(result.Content) ? null : result.Content)
                {
                    ToolCalls = toolCalls
                });

                foreach (var toolCall in toolCalls)
                {
                    await UpdateAssistantActivityAsync(
                        assistantMessage,
                        GetToolActivityText(toolCall.Name));

                    string toolResult;
                    if ((actionExecutionWasDenied || actionExecutionWasAuthorized) &&
                        toolCall.Name == ClassIslandActionAiService.ExecuteActionsToolName)
                    {
                        toolResult = JsonSerializer.Serialize(new
                        {
                            status = actionExecutionWasDenied ? "denied" : "already_executed",
                            message = actionExecutionWasDenied
                                ? "用户已拒绝本轮行动执行，不再重复询问。"
                                : "本轮已经审批并执行过一个行动批次，不允许拆分或重复执行。"
                        });
                    }
                    else if (blockedWriteStatus is not null &&
                        toolCall.Name == ClassIslandProfileAiService.PatchProfileToolName)
                    {
                        toolResult = JsonSerializer.Serialize(new
                        {
                            status = blockedWriteStatus,
                            message = blockedWriteStatus == "denied"
                                ? "用户已拒绝本轮档案写入，不再重复询问。"
                                : "本轮档案提交已经发生保存或回滚异常，为避免扩大影响，不再执行后续写入。"
                        });
                    }
                    else if (ClassIslandActionAiService.OwnsTool(toolCall.Name))
                    {
                        toolResult = await _actionAiService.ExecuteToolAsync(
                            toolCall,
                            async preview =>
                            {
                                var isAllowed = await _confirmActionExecutionAsync(preview);
                                actionExecutionWasAuthorized |= isAllowed;
                                return isAllowed;
                            },
                            listedBeforeRound,
                            describedBeforeRound,
                            listedAppSettingsBeforeRound,
                            cancellationToken);
                    }
                    else
                    {
                        toolResult = await _profileAiService.ExecuteToolAsync(
                            toolCall,
                            _confirmProfileModificationAsync,
                            cancellationToken);
                    }

                    var toolStatus = TryGetToolStatus(toolResult);
                    if (toolCall.Name == ClassIslandActionAiService.ListActionsToolName &&
                        string.Equals(toolStatus, "success", StringComparison.Ordinal))
                    {
                        AddActionIds(toolResult, listedActionIds);
                    }
                    if (toolCall.Name == ClassIslandActionAiService.DescribeActionsToolName &&
                        string.Equals(toolStatus, "success", StringComparison.Ordinal))
                    {
                        AddActionIds(toolResult, describedActionIds);
                    }
                    if (toolCall.Name == ClassIslandActionAiService.ListAppSettingsToolName &&
                        string.Equals(toolStatus, "success", StringComparison.Ordinal))
                    {
                        AddAppSettingNames(toolResult, listedAppSettingNames);
                    }
                    if (toolCall.Name == ClassIslandActionAiService.ExecuteActionsToolName)
                    {
                        actionExecutionCompleted |= toolStatus is "completed" or "partially_completed";
                        actionExecutionWasDenied |= string.Equals(toolStatus, "denied", StringComparison.Ordinal);
                    }
                    profileWasModified |= string.Equals(toolStatus, "applied", StringComparison.Ordinal);
                    profileStateIsUncertain |= string.Equals(toolStatus, "possibly_applied", StringComparison.Ordinal);
                    profileWriteWasRolledBack |= string.Equals(toolStatus, "rolled_back", StringComparison.Ordinal);
                    if (toolStatus is "denied" or "possibly_applied" or "rolled_back")
                    {
                        blockedWriteStatus = toolStatus;
                    }

                    await UpdateAssistantActivityAsync(
                        assistantMessage,
                        GetToolResultActivityText(toolCall.Name, toolStatus));

                    agentMessages.Add(new AiChatMessage("tool", toolResult)
                    {
                        ToolCallId = toolCall.Id
                    });
                }
            }

            if (!generationCompleted)
            {
                throw new InvalidOperationException($"AI 连续调用工具超过 {maximumToolRounds} 轮，已停止以避免循环执行。");
            }

            if (profileStateIsUncertain)
            {
                StatusText = "档案提交和自动回滚均发生异常，当前内容可能已改变，请立即在档案编辑器中核对。";
            }
            else if (profileWriteWasRolledBack)
            {
                StatusText = profileWasModified
                    ? "此前档案修改已保存；后一次写入失败并已自动回滚。"
                    : "档案写入失败，已自动恢复并保存修改前的内容。";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await UpdateAssistantContentAsync(
                assistantMessage,
                content.Length > 0 ? content.ToString() : streamedContent.ToString());
            StatusText = profileStateIsUncertain
                ? "档案提交和自动回滚均发生异常，当前内容可能已改变，请立即在档案编辑器中核对。"
                : profileWriteWasRolledBack
                    ? profileWasModified
                        ? "此前档案修改已保存；后一次写入失败并已回滚。"
                        : "档案写入失败，已自动恢复并保存修改前的内容。"
                    : profileWasModified
                        ? "档案修改已经保存；已停止生成后续回复"
                        : actionExecutionCompleted
                            ? "行动已执行；已停止生成后续回复"
                            : actionExecutionWasAuthorized
                                ? "已停止生成并请求中断行动；部分行动可能已经执行"
                                : "已停止生成";
        }
        catch (Exception ex)
        {
            await UpdateAssistantContentAsync(
                assistantMessage,
                content.Length > 0 ? content.ToString() : streamedContent.ToString());
            StatusText = profileStateIsUncertain
                ? "档案提交和自动回滚均发生异常，当前内容可能已改变，请立即在档案编辑器中核对。"
                : profileWriteWasRolledBack
                    ? profileWasModified
                        ? $"此前档案修改已保存；后一次写入失败并已回滚。后续回复失败：{ex.Message}"
                        : $"档案写入失败但已回滚；后续回复失败：{ex.Message}"
                    : profileWasModified
                        ? $"档案修改已经保存，但生成后续回复失败：{ex.Message}"
                        : actionExecutionCompleted
                            ? $"行动已经执行，但生成后续回复失败：{ex.Message}"
                            : actionExecutionWasAuthorized
                                ? $"行动已获允许且可能已经执行，但请求未完整结束：{ex.Message}"
                                : $"请求失败：{ex.Message}";
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                assistantMessage.ActivityText = string.Empty;
                assistantMessage.IsStreaming = false;
                if (string.IsNullOrWhiteSpace(assistantMessage.Content))
                {
                    conversation.Messages.Remove(assistantMessage);
                }
            });

            _store.Touch(conversation);
            TrySaveStore();
            _generatingConversation = null;
            IsGenerating = false;
            _generationCancellation?.Dispose();
            _generationCancellation = null;

            if (generationCompleted && IsClassIslandNotificationSharingEnabled &&
                !_suppressClassIslandNotificationSharing)
            {
                try
                {
                    await RunOnUiThreadAsync(
                        () => _notificationProvider.ShowAiReplyNotification(content.ToString()));
                }
                catch (Exception ex)
                {
                    StatusText = $"AI 回复已生成，但通知发送失败：{ex.Message}";
                }
            }
        }
    }

    private static string GetToolActivityText(string toolName)
    {
        return toolName switch
        {
            ClassIslandProfileAiService.ReadProfileToolName => "正在查看档案...",
            ClassIslandProfileAiService.PatchProfileToolName => "正在生成并校验修改预览...",
            ClassIslandActionAiService.ListActionsToolName => "正在查找可用行动...",
            ClassIslandActionAiService.DescribeActionsToolName => "正在核对行动参数...",
            ClassIslandActionAiService.ListAppSettingsToolName => "正在查找可用应用设置...",
            ClassIslandActionAiService.ExecuteActionsToolName => "正在生成并校验行动执行预览...",
            _ => "正在处理档案请求..."
        };
    }

    private static string GetToolResultActivityText(string toolName, string? status)
    {
        if (toolName == ClassIslandProfileAiService.ReadProfileToolName)
        {
            return string.Equals(status, "success", StringComparison.Ordinal)
                ? "正在理解档案..."
                : "档案读取未完成，正在整理结果...";
        }

        if (toolName == ClassIslandActionAiService.ListActionsToolName)
        {
            return string.Equals(status, "success", StringComparison.Ordinal)
                ? "正在匹配行动..."
                : "行动目录读取未完成，正在整理结果...";
        }

        if (toolName == ClassIslandActionAiService.DescribeActionsToolName)
        {
            return string.Equals(status, "success", StringComparison.Ordinal)
                ? "正在准备行动参数..."
                : "行动参数读取未完成，正在整理结果...";
        }

        if (toolName == ClassIslandActionAiService.ListAppSettingsToolName)
        {
            return string.Equals(status, "success", StringComparison.Ordinal)
                ? "正在匹配应用设置和值..."
                : "应用设置目录读取未完成，正在整理结果...";
        }

        if (toolName == ClassIslandActionAiService.ExecuteActionsToolName)
        {
            return status switch
            {
                "completed" => "行动已执行，正在核对结果...",
                "partially_completed" => "部分行动未完成，正在核对结果...",
                "denied" => "行动执行已取消，正在整理结果...",
                _ => "行动未执行，正在整理结果..."
            };
        }

        if (toolName != ClassIslandProfileAiService.PatchProfileToolName)
        {
            return "正在整理档案处理结果...";
        }

        return status switch
        {
            "applied" => "修改已保存，正在核对档案...",
            "denied" => "修改已取消，正在整理结果...",
            "rolled_back" => "写入已回滚，正在整理结果...",
            "possibly_applied" => "正在核对档案写入状态...",
            _ => "修改未完成，正在整理结果..."
        };
    }

    private static string? TryGetToolStatus(string toolResult)
    {
        try
        {
            using var document = JsonDocument.Parse(toolResult);
            return document.RootElement.TryGetProperty("status", out var status)
                ? status.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddActionIds(string toolResult, ISet<string> actionIds)
    {
        try
        {
            using var document = JsonDocument.Parse(toolResult);
            if (!document.RootElement.TryGetProperty("actions", out var actions) ||
                actions.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var action in actions.EnumerateArray())
            {
                if (action.TryGetProperty("id", out var id) &&
                    id.GetString() is { Length: > 0 } value)
                {
                    actionIds.Add(value);
                }
            }
        }
        catch (JsonException)
        {
            // The tool dispatcher will surface malformed tool results separately.
        }
    }

    private static void AddAppSettingNames(string toolResult, ISet<string> propertyNames)
    {
        try
        {
            using var document = JsonDocument.Parse(toolResult);
            if (!document.RootElement.TryGetProperty("settings", out var settings) ||
                settings.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var setting in settings.EnumerateArray())
            {
                if (setting.TryGetProperty("propertyName", out var propertyName) &&
                    propertyName.GetString() is { Length: > 0 } value)
                {
                    propertyNames.Add(value);
                }
            }
        }
        catch (JsonException)
        {
            // The tool dispatcher will surface malformed tool results separately.
        }
    }

    private Task UpdateAssistantContentAsync(AiConversationMessage message, string content)
    {
        return RunOnUiThreadAsync(() => message.Content = content);
    }

    private Task UpdateAssistantStreamingContentAsync(AiConversationMessage message, string content)
    {
        return RunOnUiThreadAsync(() =>
        {
            message.ActivityText = string.Empty;
            message.Content = content;
        });
    }

    private Task UpdateAssistantActivityAsync(AiConversationMessage message, string activityText)
    {
        return RunOnUiThreadAsync(() => message.ActivityText = activityText);
    }

    private bool TryLoadSystemPrompt(out string systemPrompt)
    {
        try
        {
            systemPrompt = _promptService.LoadSystemPrompt(_useVoiceWakePrompt);
            return true;
        }
        catch (Exception ex)
        {
            systemPrompt = string.Empty;
            StatusText = $"无法加载系统提示词：{ex.Message}";
            return false;
        }
    }

    private static AiChatMessage CreateRequestMessage(AiConversationMessage message)
    {
        if (message.Attachments.Count == 0)
        {
            return new AiChatMessage(message.Role, message.Content);
        }

        var contents = new List<AiChatContent>(message.Attachments.Count);
        foreach (var attachment in message.Attachments)
        {
            switch (attachment.Kind)
            {
                case AiAttachmentKind.Text:
                    contents.Add(new AiTextContent(CreateAttachmentText(attachment)));
                    break;
                case AiAttachmentKind.Image:
                case AiAttachmentKind.Pdf:
                    contents.Add(new AiDataContent(
                        attachment.Data ?? throw new InvalidDataException(
                            $"附件 {attachment.FileName} 缺少原始数据。"),
                        attachment.MediaType,
                        attachment.FileName));
                    break;
                default:
                    throw new InvalidDataException($"附件 {attachment.FileName} 的类型无效。");
            }
        }

        return new AiChatMessage(message.Role, message.Content)
        {
            Contents = contents
        };
    }

    private static string CreateAttachmentText(AiAttachment attachment)
    {
        var id = attachment.Id.ToString("D");
        return $"[附件开始 id={id} 文件名={JsonSerializer.Serialize(attachment.FileName)}]" +
               Environment.NewLine +
               (attachment.Text ?? string.Empty) +
               Environment.NewLine +
               $"[附件结束 id={id}]";
    }

    private static void RemoveMessagesAfter(AiConversation conversation, int messageIndex)
    {
        while (conversation.Messages.Count > messageIndex + 1)
        {
            foreach (var attachment in conversation.Messages[^1].Attachments)
            {
                attachment.Dispose();
            }

            conversation.Messages.RemoveAt(conversation.Messages.Count - 1);
        }
    }

    private static int FindPreviousUserMessageIndex(AiConversation conversation, int startIndex)
    {
        for (var index = Math.Min(startIndex - 1, conversation.Messages.Count - 1); index >= 0; index--)
        {
            if (conversation.Messages[index].IsUser)
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private void AttachConversation(AiConversation? conversation)
    {
        if (conversation is null)
        {
            return;
        }

        conversation.Messages.CollectionChanged += OnMessagesCollectionChanged;
        foreach (var message in conversation.Messages)
        {
            message.PropertyChanged += OnMessagePropertyChanged;
        }
    }

    private void DetachConversation(AiConversation? conversation)
    {
        if (conversation is null)
        {
            return;
        }

        conversation.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        foreach (var message in conversation.Messages)
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AiConversationMessage message in e.OldItems)
            {
                message.PropertyChanged -= OnMessagePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AiConversationMessage message in e.NewItems)
            {
                message.PropertyChanged += OnMessagePropertyChanged;
            }
        }

        OnPropertyChanged(nameof(HasMessages));
        ConversationContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnConversationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isDisposed || _useTransientConversation)
        {
            return;
        }

        var selectedConversationBeforeMove = e.Action == NotifyCollectionChangedAction.Move &&
                                             _operationGate.IsBusy
            ? SelectedConversation
            : null;
        if (selectedConversationBeforeMove is not null)
        {
            // ListBox may write back a different SelectedItem while processing CollectionChanged.Move.
            Dispatcher.UIThread.Post(
                () => RestoreSelectionAfterConversationMove(selectedConversationBeforeMove),
                DispatcherPriority.Background);
        }

        var previousInputText = InputText;
        var previousAttachments = PendingAttachments.ToList();
        var selectedConversationWasRemoved = SelectedConversation is not null &&
                                             !Conversations.Contains(SelectedConversation);
        var discardedSelectedDraft = selectedConversationWasRemoved &&
                                     (!string.IsNullOrEmpty(previousInputText) || previousAttachments.Count > 0);
        if (selectedConversationWasRemoved)
        {
            SelectedConversation = Conversations.FirstOrDefault(x => x.Id == _store.ActiveConversationId)
                                   ?? Conversations.FirstOrDefault();
            DisposeAttachments(previousAttachments);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var draft in _composerDrafts.Values)
            {
                DisposeAttachments(draft.Attachments);
            }
            _composerDrafts.Clear();
        }
        else if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace &&
                 e.OldItems is not null)
        {
            foreach (AiConversation removedConversation in e.OldItems)
            {
                RemoveComposerDraft(removedConversation.Id);
            }
        }

        if (discardedSelectedDraft)
        {
            StatusText = "当前对话已在另一个窗口删除，未发送的正文和附件草稿已清除";
        }
    }

    private void RestoreSelectionAfterConversationMove(AiConversation expectedConversation)
    {
        if (_isDisposed || !Conversations.Contains(expectedConversation) ||
            ReferenceEquals(SelectedConversation, expectedConversation))
        {
            return;
        }

        SelectedConversation = expectedConversation;
    }

    private void StoreComposerDraft(AiConversation? conversation)
    {
        if (conversation is null)
        {
            return;
        }

        RemoveComposerDraft(conversation.Id);
        var attachments = PendingAttachments.ToList();
        PendingAttachments.Clear();
        var text = InputText;
        InputText = string.Empty;
        if (!string.IsNullOrEmpty(text) || attachments.Count > 0)
        {
            _composerDrafts[conversation.Id] = new ComposerDraft(text, attachments);
        }
    }

    private void RestoreComposerDraft(AiConversation? conversation)
    {
        InputText = string.Empty;
        if (conversation is null || !_composerDrafts.Remove(conversation.Id, out var draft))
        {
            return;
        }

        InputText = draft.Text;
        foreach (var attachment in draft.Attachments)
        {
            PendingAttachments.Add(attachment);
        }
    }

    private void RemoveComposerDraft(Guid conversationId)
    {
        if (_composerDrafts.Remove(conversationId, out var draft))
        {
            DisposeAttachments(draft.Attachments);
        }
    }

    private static void DisposeAttachments(IEnumerable<AiAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            attachment.Dispose();
        }
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AiConversationMessage.Content) or nameof(AiConversationMessage.ActivityText))
        {
            ConversationContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TrySetActiveConversation(AiConversation? conversation)
    {
        if (_useTransientConversation)
        {
            return;
        }

        try
        {
            _store.SetActiveConversation(conversation);
        }
        catch (Exception ex)
        {
            StatusText = $"保存会话状态失败：{ex.Message}";
        }
    }

    private void TrySaveStore()
    {
        if (_useTransientConversation)
        {
            return;
        }

        try
        {
            _store.Save();
        }
        catch (Exception ex)
        {
            StatusText = $"保存对话失败：{ex.Message}";
        }
    }

    private static string CreateConversationTitle(string message)
    {
        var normalized = string.Join(' ', message
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 28;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private sealed record ComposerDraft(string Text, IReadOnlyList<AiAttachment> Attachments);
}

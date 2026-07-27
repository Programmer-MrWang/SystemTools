using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
    private readonly AiConversationStore _store;
    private readonly IOpenAiCompatibleService _aiService;
    private readonly AiPromptService _promptService;
    private readonly MainConfigHandler _configHandler;
    private readonly SystemToolsNotificationProvider _notificationProvider;
    private CancellationTokenSource? _generationCancellation;
    private Task _generationTask = Task.CompletedTask;
    private AiConversation? _generatingConversation;
    private bool _isDisposed;

    [ObservableProperty] private AiConversation? _selectedConversation;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isHistoryOpen = true;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private string _statusText = string.Empty;

    public AiChatSettingsViewModel(
        AiConversationStore store,
        IOpenAiCompatibleService aiService,
        AiPromptService promptService,
        MainConfigHandler configHandler,
        SystemToolsNotificationProvider notificationProvider)
    {
        _store = store;
        _aiService = aiService;
        _promptService = promptService;
        _configHandler = configHandler;
        _notificationProvider = notificationProvider;

        var selected = store.Conversations.FirstOrDefault(x => x.Id == store.ActiveConversationId)
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
        : "输入消息，Alt+Enter 发送";

    public bool CanSend => !IsGenerating &&
                           SelectedConversation is not null &&
                           !string.IsNullOrWhiteSpace(InputText) &&
                           !string.IsNullOrWhiteSpace(_configHandler.Data.AiModel);

    public bool IsNotGenerating => !IsGenerating;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public bool HasMessages => SelectedConversation?.Messages.Count > 0;

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

    public AiConversation CreateNewConversation()
    {
        ThrowIfDisposed();
        var conversation = _store.CreateConversation();
        SelectedConversation = conversation;
        StatusText = string.Empty;
        return conversation;
    }

    public async Task DeleteConversationAsync(AiConversation conversation)
    {
        ThrowIfDisposed();

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
        if (SelectedConversation is null)
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

        var conversation = SelectedConversation;
        var userText = InputText.Trim();
        if (!TryLoadSystemPrompt(out var systemPrompt))
        {
            return;
        }

        InputText = string.Empty;
        StatusText = string.Empty;

        var isFirstUserMessage = conversation.Messages.All(x => !x.IsUser);
        conversation.Messages.Add(new AiConversationMessage
        {
            Role = "user",
            Content = userText
        });

        if (isFirstUserMessage)
        {
            conversation.Title = CreateConversationTitle(userText);
        }

        _store.Touch(conversation);
        TrySaveStore();

        await GenerateResponseForConversationAsync(conversation, systemPrompt);
    }

    public void BeginEditUserMessage(AiConversationMessage message)
    {
        ThrowIfDisposed();
        if (IsGenerating || !message.IsUser || SelectedConversation?.Messages.Contains(message) != true)
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
        if (messageIndex < 0 || string.IsNullOrWhiteSpace(editedText))
        {
            StatusText = "消息内容不能为空";
            return;
        }

        if (!TryLoadSystemPrompt(out var systemPrompt))
        {
            return;
        }

        message.Content = editedText;
        message.DraftContent = editedText;
        message.IsEditing = false;
        RemoveMessagesAfter(conversation, messageIndex);

        if (conversation.Messages.Take(messageIndex).All(x => !x.IsUser))
        {
            conversation.Title = CreateConversationTitle(editedText);
        }

        _store.Touch(conversation);
        TrySaveStore();
        StatusText = string.Empty;
        await GenerateResponseForConversationAsync(conversation, systemPrompt);
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

        RemoveMessagesAfter(conversation, userMessageIndex);
        _store.Touch(conversation);
        TrySaveStore();
        StatusText = string.Empty;
        await GenerateResponseForConversationAsync(conversation, systemPrompt);
    }

    public void ReportError(string message)
    {
        StatusText = message;
    }

    private async Task GenerateResponseForConversationAsync(AiConversation conversation, string systemPrompt)
    {
        var requestMessages = new[] { new AiChatMessage("system", systemPrompt) }
            .Concat(conversation.Messages
                .Where(x => !string.IsNullOrWhiteSpace(x.Content))
                .Select(x => new AiChatMessage(x.Role, x.Content)))
            .ToArray();

        var assistantMessage = new AiConversationMessage
        {
            Role = "assistant",
            IsStreaming = true
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
        _generationCancellation?.Dispose();
        DetachConversation(SelectedConversation);
    }

    partial void OnSelectedConversationChanged(AiConversation? oldValue, AiConversation? newValue)
    {
        DetachConversation(oldValue);
        AttachConversation(newValue);
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
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(IsNotGenerating));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatus));
    }

    private async Task GenerateResponseAsync(
        AiConversation conversation,
        AiConversationMessage assistantMessage,
        AiChatMessage[] requestMessages,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder();
        var renderTimer = Stopwatch.StartNew();
        var generationCompleted = false;

        try
        {
            await foreach (var delta in _aiService.StreamChatCompletionAsync(
                               requestMessages,
                               cancellationToken: cancellationToken))
            {
                content.Append(delta);
                if (renderTimer.ElapsedMilliseconds < 40)
                {
                    continue;
                }

                await UpdateAssistantContentAsync(assistantMessage, content.ToString());
                renderTimer.Restart();
            }

            await UpdateAssistantContentAsync(assistantMessage, content.ToString());
            generationCompleted = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await UpdateAssistantContentAsync(assistantMessage, content.ToString());
            StatusText = "已停止生成";
        }
        catch (Exception ex)
        {
            await UpdateAssistantContentAsync(assistantMessage, content.ToString());
            StatusText = $"请求失败：{ex.Message}";
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
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

            if (generationCompleted && IsClassIslandNotificationSharingEnabled)
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

    private Task UpdateAssistantContentAsync(AiConversationMessage message, string content)
    {
        return RunOnUiThreadAsync(() => message.Content = content);
    }

    private bool TryLoadSystemPrompt(out string systemPrompt)
    {
        try
        {
            systemPrompt = _promptService.LoadSystemPrompt();
            return true;
        }
        catch (Exception ex)
        {
            systemPrompt = string.Empty;
            StatusText = $"无法加载系统提示词：{ex.Message}";
            return false;
        }
    }

    private static void RemoveMessagesAfter(AiConversation conversation, int messageIndex)
    {
        while (conversation.Messages.Count > messageIndex + 1)
        {
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

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiConversationMessage.Content))
        {
            ConversationContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TrySetActiveConversation(AiConversation? conversation)
    {
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
}

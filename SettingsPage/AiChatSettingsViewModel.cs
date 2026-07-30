using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly AiConversationStore _store;
    private readonly IOpenAiCompatibleService _aiService;
    private readonly AiPromptService _promptService;
    private readonly MainConfigHandler _configHandler;
    private readonly SystemToolsNotificationProvider _notificationProvider;
    private readonly ClassIslandProfileAiService _profileAiService;
    private readonly Func<ProfileModificationPreview, Task<bool>> _confirmProfileModificationAsync;
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
        SystemToolsNotificationProvider notificationProvider,
        ClassIslandProfileAiService profileAiService,
        Func<ProfileModificationPreview, Task<bool>> confirmProfileModificationAsync)
    {
        _store = store;
        _aiService = aiService;
        _promptService = promptService;
        _configHandler = configHandler;
        _notificationProvider = notificationProvider;
        _profileAiService = profileAiService;
        _confirmProfileModificationAsync = confirmProfileModificationAsync;

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
        var streamedContent = new StringBuilder();
        var generationCompleted = false;
        var profileWasModified = false;
        var profileStateIsUncertain = false;
        var profileWriteWasRolledBack = false;
        string? blockedWriteStatus = null;

        try
        {
            var agentMessages = requestMessages.ToList();
            const int maximumToolRounds = 8;
            const int maximumToolCallsPerRound = 8;

            for (var round = 0; round < maximumToolRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                streamedContent.Clear();
                var renderTimer = Stopwatch.StartNew();
                var hasRenderedStreamedContent = false;
                AiChatCompletionResult? result = null;

                await foreach (var update in _aiService.StreamChatCompletionWithToolsAsync(
                                   agentMessages,
                                   _profileAiService.Tools,
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

                await UpdateAssistantContentAsync(assistantMessage, content.ToString());
                streamedContent.Clear();

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
                    if (blockedWriteStatus is not null &&
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
                    else
                    {
                        toolResult = await _profileAiService.ExecuteToolAsync(
                            toolCall,
                            _confirmProfileModificationAsync,
                            cancellationToken);
                    }

                    var toolStatus = TryGetToolStatus(toolResult);
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

    private static string GetToolActivityText(string toolName)
    {
        return toolName switch
        {
            ClassIslandProfileAiService.ReadProfileToolName => "正在查看档案...",
            ClassIslandProfileAiService.PatchProfileToolName => "正在生成并校验修改预览...",
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
        if (e.PropertyName is nameof(AiConversationMessage.Content) or nameof(AiConversationMessage.ActivityText))
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

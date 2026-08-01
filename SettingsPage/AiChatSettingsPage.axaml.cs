using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using SystemTools.ConfigHandlers;
using SystemTools.Models;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo("systemtools.settings.aiChat", "AI 对话", "\uEFFF", "\uEFFF")]
public partial class AiChatSettingsPage : SettingsPageBase
{
    private const double BottomTolerance = 12;

    private bool _isDisposed;
    private bool _isAtConversationBottom = true;
    private AiConversation? _displayedConversation;

    public AiChatSettingsPage()
    {
        if (GlobalConstants.MainConfig is null)
        {
            GlobalConstants.MainConfig = new MainConfigHandler(GlobalConstants.PluginConfigFolder
                                                               ?? Path.Combine(
                                                                   Environment.GetFolderPath(Environment.SpecialFolder
                                                                       .LocalApplicationData),
                                                                   "ClassIsland",
                                                                   "Plugins",
                                                                   "SystemTools"));
        }

        ViewModel = CreateViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        _displayedConversation = ViewModel.SelectedConversation;
        ViewModel.ConversationContentChanged += ViewModel_OnConversationContentChanged;
    }

    public AiChatSettingsViewModel ViewModel { get; private set; }

    private async void SendButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void VoiceInputButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleVoiceInputAsync();
        MessageInput.Focus();
        MessageInput.CaretIndex = MessageInput.Text?.Length ?? 0;
    }

    private async void MessageInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (await TryPasteBitmapAsync())
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        e.Handled = true;
        await SendCurrentMessageAsync();
    }

    private async void AddAttachmentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBeginAttachmentUpdate())
        {
            return;
        }

        try
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider
                                  ?? throw new InvalidOperationException("无法访问文件选择器");
            var files = await storageProvider.OpenFilePickerAsync(
                AiAttachmentService.CreateFilePickerOptions());
            await AddFilesAsync(files.OfType<IStorageFile>().ToArray());
        }
        catch (Exception ex)
        {
            ViewModel.ReportError($"无法添加附件：{ex.Message}");
        }
        finally
        {
            ViewModel.EndAttachmentUpdate();
        }
    }

    private void RemovePendingAttachmentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AiAttachment attachment })
        {
            ViewModel.RemovePendingAttachment(attachment);
        }
    }

    private async Task<bool> TryPasteBitmapAsync()
    {
        if (!ViewModel.TryBeginAttachmentUpdate())
        {
            return false;
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null || await clipboard.TryGetTextAsync() is not null)
            {
                return false;
            }

            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null)
            {
                return false;
            }

            if (AiAttachmentService.TryCreatePastedBitmap(
                    bitmap,
                    ViewModel.PendingAttachments.Count,
                    ViewModel.PendingAttachmentBytes,
                    out var attachment,
                    out var error))
            {
                ViewModel.AddPendingAttachments([attachment!]);
                ViewModel.ReportError(string.Empty);
                return true;
            }

            ViewModel.ReportError(error!);
            return true;
        }
        catch (Exception ex)
        {
            ViewModel.ReportError($"无法粘贴图片：{ex.Message}");
            return true;
        }
        finally
        {
            ViewModel.EndAttachmentUpdate();
        }
    }

    private async Task AddFilesAsync(IReadOnlyList<IStorageFile> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        var result = await AiAttachmentService.LoadFilesAsync(
            files,
            ViewModel.PendingAttachments.Count,
            ViewModel.PendingAttachmentBytes);
        ViewModel.AddPendingAttachments(result.Accepted);
        ViewModel.ReportError(result.Rejected.Count == 0
            ? string.Empty
            : "以下项目未添加：" + string.Join("；", result.Rejected));
    }

    private void ChatPage_OnDragEnter(object? sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e);
        var availableSlots = Math.Max(
            0,
            AiAttachmentService.MaximumAttachmentCount - ViewModel.PendingAttachments.Count);
        var canAccept = AttachmentDropOverlay.ShowForFiles(
            files.Count,
            availableSlots,
            ViewModel.CanModifyAttachments);
        e.DragEffects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void ChatPage_OnDragLeave(object? sender, DragEventArgs e)
    {
        AttachmentDropOverlay.Hide();
    }

    private async void ChatPage_OnDrop(object? sender, DragEventArgs e)
    {
        AttachmentDropOverlay.Hide();
        var files = GetDroppedFiles(e);
        if (files.Count == 0 || !ViewModel.TryBeginAttachmentUpdate())
        {
            return;
        }

        try
        {
            var topLevel = TopLevel.GetTopLevel(this)
                           ?? throw new InvalidOperationException("无法访问当前窗口");
            var result = await AiAttachmentDropService.LoadAndConfirmAsync(
                topLevel,
                files,
                ViewModel.PendingAttachments.Count,
                ViewModel.PendingAttachmentBytes);
            if (result is null)
            {
                return;
            }

            ViewModel.AddPendingAttachments(result.Accepted);
            ViewModel.ReportError(result.Rejected.Count == 0
                ? string.Empty
                : "以下项目未添加：" + string.Join("；", result.Rejected));
        }
        catch (Exception ex)
        {
            ViewModel.ReportError($"无法添加拖入的附件：{ex.Message}");
        }
        finally
        {
            ViewModel.EndAttachmentUpdate();
        }
    }

    private static IReadOnlyList<IStorageFile> GetDroppedFiles(DragEventArgs e)
    {
        return e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToArray() ?? [];
    }

    private async void CopyMessageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: AiConversationMessage message })
        {
            return;
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                throw new InvalidOperationException("无法访问系统剪贴板");
            }

            await clipboard.SetTextAsync(message.Content);
        }
        catch (Exception ex)
        {
            ViewModel.ReportError($"复制失败：{ex.Message}");
        }
    }

    private async void RetryMessageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AiConversationMessage message })
        {
            var generationTask = ViewModel.RetryAssistantMessageAsync(message);
            ScrollToConversationBottom();
            await generationTask;
        }
    }

    private void EditMessageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AiConversationMessage message })
        {
            ViewModel.BeginEditUserMessage(message);
        }
    }

    private async void ConfirmEditMessageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AiConversationMessage message })
        {
            var generationTask = ViewModel.CommitEditedUserMessageAsync(message);
            ScrollToConversationBottom();
            await generationTask;
        }
    }

    private void CancelEditMessageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AiConversationMessage message })
        {
            ViewModel.CancelEditUserMessage(message);
        }
    }

    private async void EditedMessageInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            sender is not TextBox { DataContext: AiConversationMessage message })
        {
            return;
        }

        e.Handled = true;
        var generationTask = ViewModel.CommitEditedUserMessageAsync(message);
        ScrollToConversationBottom();
        await generationTask;
    }

    private void StopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.StopGeneration();
    }

    private void ToggleHistoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsHistoryOpen = !ViewModel.IsHistoryOpen;
    }

    private void NewConversationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.CreateNewConversation();
        ScrollToConversationBottom();
    }

    private void ReturnToBottomButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ScrollToConversationBottom();
    }

    private void MessageScrollViewer_OnLoaded(object? sender, RoutedEventArgs e)
    {
        ScrollToConversationBottom();
    }

    private void MessageScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateConversationBottomState();
    }

    private async void DeleteConversationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: AiConversation conversation })
        {
            return;
        }

        var dialog = new FAContentDialog
        {
            Title = "删除对话",
            Content = $"确定要删除“{conversation.Title}”吗？此操作无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };

        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this));
        if (result == FAContentDialogResult.Primary)
        {
            await ViewModel.DeleteConversationAsync(conversation);
        }
    }

    private void ConversationTitle_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel.SaveConversationTitle();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_isDisposed)
        {
            return;
        }

        ViewModel.ConversationContentChanged -= ViewModel_OnConversationContentChanged;
        ViewModel.StopGeneration();
        ViewModel.Dispose();
        _isDisposed = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_isDisposed)
        {
            return;
        }

        ViewModel = CreateViewModel();
        DataContext = ViewModel;
        _displayedConversation = ViewModel.SelectedConversation;
        ViewModel.ConversationContentChanged += ViewModel_OnConversationContentChanged;
        _isDisposed = false;
        ScrollToConversationBottom();
    }

    private AiChatSettingsViewModel CreateViewModel()
    {
        return new AiChatSettingsViewModel(
            IAppHost.GetService<AiConversationStore>(),
            IAppHost.GetService<IOpenAiCompatibleService>(),
            IAppHost.GetService<AiPromptService>(),
            IAppHost.GetService<AiChatOperationGate>(),
            IAppHost.GetService<VoskSpeechService>(),
            GlobalConstants.MainConfig!,
            IAppHost.GetService<SystemToolsNotificationProvider>(),
            IAppHost.GetService<ClassIslandProfileAiService>(),
            IAppHost.GetService<ClassIslandActionAiService>(),
            ConfirmProfileModificationAsync,
            ConfirmActionExecutionAsync);
    }

    private async Task SendCurrentMessageAsync()
    {
        var generationTask = ViewModel.SendAsync();
        ScrollToConversationBottom();
        await generationTask;
    }

    private Task<bool> ConfirmProfileModificationAsync(ProfileModificationPreview preview)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowProfileModificationDialogAsync(preview);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.SetResult(await ShowProfileModificationDialogAsync(preview));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private async Task<bool> ShowProfileModificationDialogAsync(ProfileModificationPreview preview)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _isDisposed)
        {
            return false;
        }

        var operationText = string.Join(
            Environment.NewLine + Environment.NewLine,
            preview.Operations.Select(operation =>
                operation.Operation switch
                {
                    "add" => $"ADD {operation.Path}\n  新值：{operation.After}",
                    "remove" => $"REMOVE {operation.Path}\n  原值：{operation.Before}",
                    _ => $"REPLACE {operation.Path}\n  原值：{operation.Before}\n  新值：{operation.After}"
                }));
        var dialog = new FAContentDialog
        {
            Title = "允许 AI 修改 ClassIsland 档案？",
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 620,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"档案文件：{preview.ProfileFilePath}\n修改说明：{preview.Summary}",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new ScrollViewer
                    {
                        MaxHeight = 260,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = operationText,
                            FontFamily = new Avalonia.Media.FontFamily("Consolas"),
                            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
                        }
                    },
                    new TextBlock
                    {
                        Text = "风险：AI 可能误解指令；课表、时间表或教师信息的错误修改可能立即影响显示、提醒和自动化。保存过程并非事务性，也不保证本次修改可由 .bak 完整撤销。请确认上方路径和值准确后再允许。",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = "允许并保存",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };

        return await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary;
    }

    private Task<bool> ConfirmActionExecutionAsync(ActionExecutionPreview preview)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowActionExecutionDialogAsync(preview);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.SetResult(await ShowActionExecutionDialogAsync(preview));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private async Task<bool> ShowActionExecutionDialogAsync(ActionExecutionPreview preview)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || _isDisposed)
        {
            return false;
        }

        var actionText = string.Join(
            Environment.NewLine + Environment.NewLine,
            preview.Items.Select(item =>
                $"{item.Index}. {item.Name}\nID: {item.Id}\n参数: {item.SettingsJson}"));
        var dialog = new FAContentDialog
        {
            Title = preview.Items.Count == 1
                ? "允许 AI 执行此行动？"
                : $"允许 AI 执行这 {preview.Items.Count} 项行动？",
            Content = new StackPanel
            {
                Spacing = 12,
                MaxWidth = 640,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"执行说明：{preview.Summary}",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new ScrollViewer
                    {
                        MaxHeight = 320,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = actionText,
                            FontFamily = new Avalonia.Media.FontFamily("Consolas"),
                            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
                        }
                    },
                    new TextBlock
                    {
                        Text = "这些行动可能启动程序、模拟输入、修改文件或系统状态。允许后将按上方顺序立即执行；请确认行动 ID 和参数符合你的要求。",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = "允许执行",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };

        return await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary;
    }

    private void ViewModel_OnConversationContentChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(_displayedConversation, ViewModel.SelectedConversation))
        {
            _displayedConversation = ViewModel.SelectedConversation;
            ScrollToConversationBottom();
            return;
        }

        if (_isAtConversationBottom)
        {
            ScrollToConversationBottom();
            return;
        }

        Dispatcher.UIThread.Post(UpdateConversationBottomState, DispatcherPriority.Background);
    }

    private void ScrollToConversationBottom()
    {
        _isAtConversationBottom = true;
        ReturnToBottomButton.IsVisible = false;
        Dispatcher.UIThread.Post(() =>
        {
            MessageScrollViewer.ScrollToEnd();
            UpdateConversationBottomState();
        }, DispatcherPriority.Background);
    }

    private void UpdateConversationBottomState()
    {
        var maximumOffset = Math.Max(
            0,
            MessageScrollViewer.Extent.Height - MessageScrollViewer.Viewport.Height);
        _isAtConversationBottom = maximumOffset <= BottomTolerance ||
                                  MessageScrollViewer.Offset.Y >= maximumOffset - BottomTolerance;
        ReturnToBottomButton.IsVisible = !_isAtConversationBottom;
    }
}

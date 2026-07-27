using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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

        ViewModel = new AiChatSettingsViewModel(
            IAppHost.GetService<AiConversationStore>(),
            IAppHost.GetService<IOpenAiCompatibleService>(),
            IAppHost.GetService<AiPromptService>(),
            GlobalConstants.MainConfig,
            IAppHost.GetService<SystemToolsNotificationProvider>());
        DataContext = ViewModel;
        InitializeComponent();

        _displayedConversation = ViewModel.SelectedConversation;
        ViewModel.ConversationContentChanged += ViewModel_OnConversationContentChanged;
    }

    public AiChatSettingsViewModel ViewModel { get; }

    private async void SendButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void MessageInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        e.Handled = true;
        await SendCurrentMessageAsync();
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

    private async Task SendCurrentMessageAsync()
    {
        var generationTask = ViewModel.SendAsync();
        ScrollToConversationBottom();
        await generationTask;
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

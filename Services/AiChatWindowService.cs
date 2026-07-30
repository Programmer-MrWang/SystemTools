using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using SystemTools.ConfigHandlers;
using SystemTools.Views;

namespace SystemTools.Services;

public sealed class AiChatWindowService(
    AiConversationStore store,
    IOpenAiCompatibleService aiService,
    AiPromptService promptService,
    AiChatOperationGate operationGate,
    MainConfigHandler configHandler,
    SystemToolsNotificationProvider notificationProvider,
    ClassIslandProfileAiService profileAiService,
    ClassIslandActionAiService actionAiService)
{
    private AiChatFloatingWindow? _window;

    public async Task ShowAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_window is null)
            {
                _window = new AiChatFloatingWindow(
                    store,
                    aiService,
                    promptService,
                    operationGate,
                    configHandler,
                    notificationProvider,
                    profileAiService,
                    actionAiService);
                _window.Closed += Window_OnClosed;
            }

            _window.BringToFront();
        });
    }

    public void Close()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _window?.Close();
            return;
        }

        Dispatcher.UIThread.Post(() => _window?.Close());
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            _window.Closed -= Window_OnClosed;
            _window = null;
        }
    }
}

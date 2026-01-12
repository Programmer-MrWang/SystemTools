/*using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Controls;

namespace SystemTools.Controls;

public class ShutdownDialogControl : UserControl
{
    private readonly ICommonDialogService _dialogService;
    private readonly Func<Task> _cancelAction;
    private readonly Func<Task> _extendAction;

    public TextBlock MessageText { get; }

    public ShutdownDialogControl(
        ICommonDialogService dialogService,
        string initialMessage,
        Func<Task> cancelAction,
        Func<Task> extendAction)
    {
        _dialogService = dialogService;
        _cancelAction = cancelAction;
        _extendAction = extendAction;

        var panel = new StackPanel { Spacing = 15 };

        MessageText = new TextBlock
        {
            Text = initialMessage,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(MessageText);

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        var readButton = new Button { Content = "已阅", Width = 80 };
        readButton.Click += async (s, e) => await _dialogService.CloseDialogAsync();

        var cancelButton = new Button { Content = "取消计划", Width = 100 };
        cancelButton.Click += async (s, e) =>
        {
            await _cancelAction();
            await _dialogService.CloseDialogAsync();
        };

        var extendButton = new Button { Content = "延长两分钟", Width = 100 };
        extendButton.Click += async (s, e) =>
        {
            await _extendAction();
            var newTime = DateTime.Now.AddMinutes(2);
            MessageText.Text = $"将在2分钟后关机（{newTime:HH:mm}）……";
        };

        buttonPanel.Children.Add(readButton);
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(extendButton);
        panel.Children.Add(buttonPanel);

        Content = panel;
    }
}*/
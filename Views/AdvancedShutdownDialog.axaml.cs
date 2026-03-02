using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SystemTools.Views;

public partial class AdvancedShutdownDialog : Window
{
    public AdvancedShutdownDialog()
    {
        InitializeComponent();
    }

    public TextBlock CountdownTextBlock => this.FindControl<TextBlock>("CountdownTextBlockElement")!;
    public ProgressBar CountdownProgressBar => this.FindControl<ProgressBar>("CountdownProgressBarElement")!;
    public Button ReadButton => this.FindControl<Button>("ReadButtonElement")!;
    public Button CancelPlanButton => this.FindControl<Button>("CancelPlanButtonElement")!;
    public Button ExtendButton => this.FindControl<Button>("ExtendButtonElement")!;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

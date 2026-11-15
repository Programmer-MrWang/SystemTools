using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using SystemTools.Contracts;

namespace SystemTools;

public sealed class SystemAuthenticationSettings
{
    public bool IsConfigured { get; set; }
}

[AuthorizeProviderInfo("systemtools.authProviders.systemAuthentication", "系统认证", "")]
public sealed class NativeSystemAuthorizer : AuthorizeProviderControlBase<SystemAuthenticationSettings>
{
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private IDisposable? _selection;
    private CancellationTokenSource? _request;
    private bool _selected;

    public NativeSystemAuthorizer()
    {
        var panel = new StackPanel { Spacing = 12 };
        var verify = new Button { Content = "检查或验证系统身份" };
        verify.Click += async (_, _) => await VerifyAsync();
        panel.Children.Add(_status); panel.Children.Add(verify); Content = panel;
    }
    protected override void OnLoaded(RoutedEventArgs args)
    {
        base.OnLoaded(args);
        if (IsEditingMode) return;
        var row = this.FindAncestorOfType<ListBoxItem>();
        if (row is null) { _status.Text = "请重新选择认证项。"; return; }
        _selection = row.GetObservable(ListBoxItem.IsSelectedProperty).Subscribe(selected =>
        {
            _selected = selected;
            _request?.Cancel();
            if (selected) _ = VerifyAsync();
        });
    }
    protected override void OnUnloaded(RoutedEventArgs args)
    {
        _selected = false; _request?.Cancel(); _selection?.Dispose(); _selection = null;
        base.OnUnloaded(args);
    }
    private async Task VerifyAsync()
    {
        if (!IsEditingMode && !_selected) return;
        _request?.Cancel();
        var media = IAppHost.GetService<PlatformMediaServices>();
        var source = CancellationTokenSource.CreateLinkedTokenSource(media.Stopping);
        _request = source;
        try
        {
            var auth = media.Authentication;
            var status = auth is IAsyncCapabilityProbe asynchronous ? await asynchronous.ProbeAsync(source.Token) : auth.Probe();
            if (IsEditingMode)
            {
                Settings.IsConfigured = status.CanExecute;
                _status.Text = status.CanExecute ? "系统认证可以使用。" : status.Reason;
                return;
            }
            if (!status.CanExecute) { _status.Text = status.Reason; return; }
            var result = await auth.VerifyAsync("请验证身份以继续 ClassIsland 操作", TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? 0, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (!_selected || !ReferenceEquals(_request, source)) return;
            _status.Text = result.Message;
            if (result.Verified && CompleteAuthorizeCommand is RoutedCommand command && command.CanExecute(null, this))
                command.Execute(null, this);
        }
        catch (OperationCanceledException) { _status.Text = "系统认证已取消。"; }
        catch (Exception) { _status.Text = "系统认证未完成，请重新选择后重试。"; }
        finally { if (ReferenceEquals(_request, source)) _request = null; source.Dispose(); }
    }
    public override bool ValidateAuthorizeSettings() => Settings.IsConfigured;
}

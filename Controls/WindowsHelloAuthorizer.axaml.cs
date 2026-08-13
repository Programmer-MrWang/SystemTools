using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.Services;
using SystemTools.Settings;
using Windows.Security.Credentials.UI;

namespace SystemTools.Controls;

[AuthorizeProviderInfo("systemtools.authProviders.windowsHello", "Windows Hello 验证器", "\uEED5")]
public partial class WindowsHelloAuthorizer : AuthorizeProviderControlBase<WindowsHelloSettings>
{
    private int _verificationRunning;
    private int _isLoaded;
    private int _isSelected;
    private int _selectionGeneration;
    private IDisposable? _selectionSubscription;

    public WindowsHelloAuthorizer()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Interlocked.Exchange(ref _isLoaded, 1);

        if (IsEditingMode)
        {
            await CheckConfigurationAsync();
        }
        else
        {
            var listBoxItem = this.FindAncestorOfType<ListBoxItem>();
            if (listBoxItem == null)
            {
                Settings.HasError = true;
                Settings.StatusMessage = "无法确定当前认证项，请重新选择 Windows Hello。";
                return;
            }

            _selectionSubscription = listBoxItem.GetObservable(ListBoxItem.IsSelectedProperty)
                .Subscribe(isSelected =>
                {
                    Interlocked.Exchange(ref _isSelected, isSelected ? 1 : 0);
                    var selectionGeneration = Interlocked.Increment(ref _selectionGeneration);
                    if (isSelected && Volatile.Read(ref _isLoaded) != 0)
                    {
                        _ = VerifyAsync(selectionGeneration);
                    }
                });

            // GetObservable normally emits the current value immediately; this also covers custom hosts that do not.
            if (listBoxItem.IsSelected && Volatile.Read(ref _isSelected) == 0)
            {
                Interlocked.Exchange(ref _isSelected, 1);
                var selectionGeneration = Interlocked.Increment(ref _selectionGeneration);
                _ = VerifyAsync(selectionGeneration);
            }
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        Interlocked.Exchange(ref _isLoaded, 0);
        Interlocked.Exchange(ref _isSelected, 0);
        Interlocked.Increment(ref _selectionGeneration);
        _selectionSubscription?.Dispose();
        _selectionSubscription = null;
        base.OnUnloaded(e);
    }

    private async void OnCheckConfigurationClick(object? sender, RoutedEventArgs e)
    {
        await EnrollAsync();
    }

    private void OnOpenWindowsHelloSettingsClick(object? sender, RoutedEventArgs e)
    {
        WindowsHelloService.OpenWindowsHelloSettings();
    }

    private async void OnVerifyClick(object? sender, RoutedEventArgs e)
    {
        await VerifyAsync(Volatile.Read(ref _selectionGeneration));
    }

    private async Task CheckConfigurationAsync()
    {
        Settings.Operating = true;
        Settings.HasError = false;
        Settings.StatusMessage = "正在检查 Windows Hello 人脸配置…";

        try
        {
            var support = await WindowsHelloService.CheckSupportAsync(requireFaceEnrollment: true);
            if (Volatile.Read(ref _isLoaded) == 0)
            {
                return;
            }

            Settings.HasError = !support.IsAvailable;
            Settings.StatusMessage = support.Message;
            if (!support.IsAvailable)
            {
                Settings.IsConfigured = false;
            }
        }
        catch (Exception ex)
        {
            Settings.HasError = true;
            Settings.StatusMessage = $"检查 Windows Hello 时发生错误：{ex.Message}";
            Settings.IsConfigured = false;
        }
        finally
        {
            Settings.Operating = false;
        }
    }

    private async Task EnrollAsync()
    {
        if (Interlocked.CompareExchange(ref _verificationRunning, 1, 0) != 0)
        {
            return;
        }

        var wasConfigured = Settings.IsConfigured;
        Settings.Operating = true;
        Settings.HasError = false;
        Settings.StatusMessage = "正在检查 Windows Hello 人脸配置…";

        try
        {
            var support = await WindowsHelloService.CheckSupportAsync(requireFaceEnrollment: true);
            if (Volatile.Read(ref _isLoaded) == 0)
            {
                return;
            }

            if (!support.IsAvailable)
            {
                Settings.IsConfigured = false;
                Settings.HasError = true;
                Settings.StatusMessage = support.Message;
                return;
            }

            Settings.StatusMessage = "请在 Windows 安全窗口中完成一次验证…";
            var windowHandle = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? 0;
            var result = await WindowsHelloService.RequestVerificationAsync(
                windowHandle,
                "请使用 Windows Hello 验证，以绑定 ClassIsland 认证方式");
            if (Volatile.Read(ref _isLoaded) == 0)
            {
                return;
            }

            if (result == UserConsentVerificationResult.Verified)
            {
                Settings.IsConfigured = true;
                Settings.StatusMessage = "Windows Hello 验证器已配置。人脸数据仍由 Windows 安全保管。";
                return;
            }

            Settings.IsConfigured = wasConfigured;
            Settings.HasError = true;
            Settings.StatusMessage = GetVerificationFailureMessage(result);
        }
        catch (Exception ex)
        {
            Settings.IsConfigured = wasConfigured;
            Settings.HasError = true;
            Settings.StatusMessage = $"配置 Windows Hello 时发生错误：{ex.Message}";
        }
        finally
        {
            Settings.Operating = false;
            Interlocked.Exchange(ref _verificationRunning, 0);
        }
    }

    private async Task VerifyAsync(int selectionGeneration)
    {
        if (Interlocked.CompareExchange(ref _verificationRunning, 1, 0) != 0)
        {
            return;
        }

        Settings.Operating = true;
        Settings.HasError = false;
        Settings.StatusMessage = "请在 Windows 安全窗口中完成验证…";

        try
        {
            var support = await WindowsHelloService.CheckSupportAsync(requireFaceEnrollment: true);
            if (!IsCurrentSelection(selectionGeneration))
            {
                return;
            }
            if (!support.IsAvailable)
            {
                Settings.HasError = true;
                Settings.StatusMessage = support.Message;
                return;
            }

            var windowHandle = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? 0;
            var result = await WindowsHelloService.RequestVerificationAsync(
                windowHandle,
                "请使用 Windows Hello 验证，以继续 ClassIsland 操作");
            if (!IsCurrentSelection(selectionGeneration))
            {
                return;
            }

            switch (result)
            {
                case UserConsentVerificationResult.Verified:
                    var authorizeCommandExecuted = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (AuthorizeProviderControlBase.CompleteAuthorizeCommand is not RoutedCommand command ||
                            !command.CanExecute(null, this))
                        {
                            return false;
                        }

                        // 自动验证没有焦点元素，明确从当前控件开始路由，确保命令到达认证窗口。
                        command.Execute(null, this);
                        return true;
                    });

                    if (!authorizeCommandExecuted)
                    {
                        Settings.HasError = true;
                        Settings.StatusMessage = "Windows Hello 已验证，但认证窗口未能完成放行，请重试。";
                    }
                    break;
                case UserConsentVerificationResult.Canceled:
                    Settings.HasError = true;
                    Settings.StatusMessage = "已取消 Windows Hello 验证。";
                    break;
                case UserConsentVerificationResult.RetriesExhausted:
                    Settings.HasError = true;
                    Settings.StatusMessage = "验证尝试次数过多，请稍后重试。";
                    break;
                case UserConsentVerificationResult.DeviceBusy:
                    Settings.HasError = true;
                    Settings.StatusMessage = "Windows Hello 设备正忙，请稍后重试。";
                    break;
                case UserConsentVerificationResult.DeviceNotPresent:
                    Settings.HasError = true;
                    Settings.StatusMessage = "Windows Hello 设备当前不可用。";
                    break;
                case UserConsentVerificationResult.DisabledByPolicy:
                    Settings.HasError = true;
                    Settings.StatusMessage = "Windows Hello 已被系统策略禁用。";
                    break;
                case UserConsentVerificationResult.NotConfiguredForUser:
                    Settings.HasError = true;
                    Settings.StatusMessage = "当前用户尚未配置 Windows Hello。";
                    break;
                default:
                    Settings.HasError = true;
                    Settings.StatusMessage = GetVerificationFailureMessage(result);
                    break;
            }
        }
        catch (Exception ex)
        {
            Settings.HasError = true;
            Settings.StatusMessage = $"调用 Windows Hello 时发生错误：{ex.Message}";
        }
        finally
        {
            Settings.Operating = false;
            Interlocked.Exchange(ref _verificationRunning, 0);

            // If selection changed while the system dialog was open, start the newly selected
            // Windows Hello item after the previous request has fully completed.
            var currentGeneration = Volatile.Read(ref _selectionGeneration);
            if (currentGeneration != selectionGeneration && IsCurrentSelection(currentGeneration))
            {
                _ = VerifyAsync(currentGeneration);
            }
        }
    }

    private bool IsCurrentSelection(int selectionGeneration) =>
        Volatile.Read(ref _isLoaded) != 0 &&
        Volatile.Read(ref _isSelected) != 0 &&
        Volatile.Read(ref _selectionGeneration) == selectionGeneration;

    private static string GetVerificationFailureMessage(UserConsentVerificationResult result) => result switch
    {
        UserConsentVerificationResult.Canceled => "已取消 Windows Hello 验证。",
        UserConsentVerificationResult.RetriesExhausted => "验证尝试次数过多，请稍后重试。",
        UserConsentVerificationResult.DeviceBusy => "Windows Hello 设备正忙，请稍后重试。",
        UserConsentVerificationResult.DeviceNotPresent => "Windows Hello 设备当前不可用。",
        UserConsentVerificationResult.DisabledByPolicy => "Windows Hello 已被系统策略禁用。",
        UserConsentVerificationResult.NotConfiguredForUser => "当前用户尚未配置 Windows Hello。",
        _ => "Windows Hello 未能完成验证，请重试。"
    };

    public override bool ValidateAuthorizeSettings() => Settings.IsConfigured;
}

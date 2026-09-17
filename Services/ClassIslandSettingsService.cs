using System;
using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Shared;

namespace SystemTools.Services;

public sealed class ClassIslandSettingsService
{
    private const string PrimaryColorPropertyName = "PrimaryColor";
    private const string ColorSourcePropertyName = "ColorSource";
    private const string ThemePropertyName = "Theme";

    /// <summary>主题色来源为“自定义”时的取值。</summary>
    public const int CustomColorSource = 0;

    private object? _subscribedSettings;
    private PropertyChangedEventHandler? _settingsPropertyChangedHandler;
    private bool _isApplyingOwnChange;

    /// <summary>
    /// 应用设置对象发生变更时触发。仅在变更来自宿主自身时才有意义，
    /// 由本服务发起的写入不会触发该事件。
    /// </summary>
    public event EventHandler<string>? SettingsPropertyChanged;

    public bool SetTheme(int theme) => SetProperty(ThemePropertyName, theme);

    public bool SetMainWindowVisible(bool isVisible) => SetProperty("IsMainWindowVisible", isVisible);

    public bool? GetMainWindowVisible() => GetProperty<bool>("IsMainWindowVisible");

    /// <summary>读取应用当前的强调色。</summary>
    public Color? GetPrimaryColor()
    {
        var settings = GetSettings();
        var property = settings?.GetType()
            .GetProperty(PrimaryColorPropertyName, BindingFlags.Instance | BindingFlags.Public);
        return property?.CanRead == true && property.GetValue(settings) is Color color ? color : null;
    }

    /// <summary>
    /// 读取应用当前的主题色来源。
    /// <list type="bullet">
    /// <item>0 - 自定义</item>
    /// <item>1 - 系统壁纸（已停用）</item>
    /// <item>2 - 系统强调色</item>
    /// <item>3 - 屏幕主题色（已停用）</item>
    /// </list>
    /// </summary>
    public int? GetColorSource() => GetProperty<int>(ColorSourcePropertyName);

    /// <summary>
    /// 把强调色写入应用设置。返回 <c>true</c> 表示设置当前已是该颜色
    /// （无论是否需要实际写入），<c>false</c> 表示属性不可用。
    /// </summary>
    public bool SetPrimaryColor(Color color) =>
        EnsureProperty(PrimaryColorPropertyName, color, out _);

    /// <summary>
    /// 把主题色来源写入应用设置。返回 <c>true</c> 表示设置当前已是该来源
    /// （无论是否需要实际写入），<c>false</c> 表示属性不可用。
    /// </summary>
    public bool SetColorSource(int colorSource) =>
        EnsureProperty(ColorSourcePropertyName, colorSource, out _);

    /// <summary>
    /// 订阅应用设置的属性变更。用于识别用户是否在宿主界面手动改动了主题色。
    /// </summary>
    public void SubscribeSettingsChanges()
    {
        var settings = GetSettings();
        if (settings is null || ReferenceEquals(settings, _subscribedSettings))
        {
            return;
        }

        UnsubscribeSettingsChanges();

        if (settings is INotifyPropertyChanged notifier)
        {
            _settingsPropertyChangedHandler = OnSettingsPropertyChanged;
            notifier.PropertyChanged += _settingsPropertyChangedHandler;
            _subscribedSettings = settings;
        }
    }

    /// <summary>取消订阅应用设置的属性变更。</summary>
    public void UnsubscribeSettingsChanges()
    {
        if (_subscribedSettings is INotifyPropertyChanged notifier &&
            _settingsPropertyChangedHandler is not null)
        {
            notifier.PropertyChanged -= _settingsPropertyChangedHandler;
        }

        _subscribedSettings = null;
        _settingsPropertyChangedHandler = null;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingOwnChange || string.IsNullOrEmpty(e.PropertyName))
        {
            return;
        }

        SettingsPropertyChanged?.Invoke(this, e.PropertyName);
    }

    public IDisposable? HideMainWindow()
    {
        var previous = GetMainWindowVisible();
        if (previous is null)
        {
            return null;
        }

        // Setting the same value is reported as "unchanged" by the host
        // settings object. That is still a successful hide operation from the
        // caller's perspective; the lease must remember the original state.
        if (previous.Value && !SetMainWindowVisible(false))
        {
            return null;
        }

        return new RestoreMainWindowVisibility(this, previous.Value);
    }

    public bool? GetWindowCaptureBlockingEnabled() =>
        GetProperty<bool>("IsWindowCaptureBlockingEnabled");

    public string? GetSelectedSpeechProvider()
    {
        var settings = GetSettings();
        var property = settings?.GetType()
            .GetProperty("SelectedSpeechProvider", BindingFlags.Instance | BindingFlags.Public);
        return property?.CanRead == true ? property.GetValue(settings) as string : null;
    }

    private static T? GetProperty<T>(string propertyName) where T : struct
    {
        var settings = GetSettings();
        var property = settings?.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        return property?.CanRead == true && property.GetValue(settings) is T value ? value : null;
    }

    /// <summary>
    /// 写入设置项。返回值表示本次调用是否真的改变了值；
    /// 属性不存在、不可写或新值与旧值相同时返回 <c>false</c>。
    /// </summary>
    private bool SetProperty<T>(string propertyName, T value) =>
        EnsureProperty(propertyName, value, out var changed) && changed;

    /// <summary>
    /// 把设置项置为指定值。
    /// </summary>
    /// <returns>属性存在且可写时返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    private bool EnsureProperty<T>(string propertyName, T value, out bool changed)
    {
        changed = false;

        var settings = GetSettings();
        var property = settings?.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanRead != true || property.CanWrite != true)
        {
            return false;
        }

        if (Equals(property.GetValue(settings), value))
        {
            // 已经是要设置的值，无需写入。
            return true;
        }

        // 写入期间抑制自身触发的变更通知，避免把插件自己的修改误判为用户手动改色。
        _isApplyingOwnChange = true;
        try
        {
            property.SetValue(settings, value);
            changed = true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _isApplyingOwnChange = false;
        }

        return true;
    }

    private static object? GetSettings()
    {
        var serviceType = GetSettingsServiceType();
        if (serviceType is null)
        {
            return null;
        }

        var settingsService = IAppHost.Host?.Services.GetService(serviceType);
        if (settingsService is null)
        {
            return null;
        }

        return serviceType
            .GetProperty("Settings", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(settingsService);
    }

    /// <summary>
    /// 解析宿主的应用设置服务类型。优先从主窗口所在的程序集查找，
    /// 主窗口尚未创建时回退到已加载的主程序集，保证启动早期也能读写设置。
    /// </summary>
    private static Type? GetSettingsServiceType()
    {
        const string typeName = "ClassIsland.Services.SettingsService";

        // 宿主尚未初始化时 Application.Current 可能不是 ClassIsland 的应用实例，
        // 此时直接回退到程序集扫描，避免抛出异常。
        var resolved = (Application.Current as AppBase)?.MainWindow?.GetType().Assembly
            .GetType(typeName);
        if (resolved is not null)
        {
            return resolved;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            resolved = assembly.GetType(typeName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private sealed class RestoreMainWindowVisibility(
        ClassIslandSettingsService service,
        bool previousValue) : IDisposable
    {
        private ClassIslandSettingsService? _service = service;

        public void Dispose()
        {
            var owner = System.Threading.Interlocked.Exchange(ref _service, null);
            if (owner is null)
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                owner.SetMainWindowVisible(previousValue);
            }
            else
            {
                Dispatcher.UIThread.Post(() => owner.SetMainWindowVisible(previousValue));
            }
        }
    }
}

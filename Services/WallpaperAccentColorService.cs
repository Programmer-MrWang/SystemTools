using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SystemTools.ConfigHandlers;
using SystemTools.Helpers;

namespace SystemTools.Services;

/// <summary>
/// 从桌面壁纸提取主色调，并将其应用为 ClassIsland 的主题色。
/// </summary>
/// <remarks>
/// <para>
/// 提取到的颜色会写入宿主的<b>自定义强调色</b>设置并保存，交由 ClassIsland 自身维护。
/// 这样重启后依然生效，也不会被宿主在设置变动时的主题重算流程覆盖。
/// </para>
/// <para>
/// 接管前会记录原始强调色设置，并持久化到插件配置中；关闭开关时还原该设置。
/// 应用退出时<b>不</b>还原，因为用户期望壁纸主题色在下次启动时继续生效。
/// </para>
/// <para>
/// 壁纸图片不可读或没有可提取的色调时（例如 Wallpaper Engine 之类的动态壁纸），
/// 保持当前主题色不变。
/// </para>
/// </remarks>
public sealed class WallpaperAccentColorService(
    MainConfigHandler configHandler,
    ClassIslandSettingsService classIslandSettingsService,
    ILogger<WallpaperAccentColorService> logger)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>提取失败后的重试间隔，避免壁纸不可用时持续解码。</summary>
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromSeconds(30);

    private readonly MainConfigHandler _configHandler = configHandler;
    private readonly ClassIslandSettingsService _settingsService = classIslandSettingsService;
    private readonly ILogger<WallpaperAccentColorService> _logger = logger;

    private readonly DispatcherTimer _timer = new() { Interval = PollInterval };
    private readonly SemaphoreSlim _workLock = new(1, 1);

    /// <summary>本服务最近一次写入的强调色，用于区分插件写入与用户手动修改。仅在 UI 线程访问。</summary>
    private Color? _lastAppliedColor;

    /// <summary>是否已经成功写入过一次。后台线程据此决定是否需要重新提取。</summary>
    private volatile bool _hasAppliedOnce;

    private DateTime _lastWallpaperWriteTimeUtc = DateTime.MinValue;
    private DateTime? _lastFailedAttemptUtc;
    private bool _isSubscribed;

    private MainConfigData Config => _configHandler.Data;

    /// <summary>启动服务。根据当前配置决定是否接管主题色。</summary>
    public void Start()
    {
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        ApplyConfig();
    }

    /// <summary>
    /// 停止服务。此处刻意不还原强调色设置，以保留用户已选择的壁纸主题色；
    /// 还原只在用户关闭开关时发生。
    /// </summary>
    public void Stop()
    {
        _timer.Stop();
        Unsubscribe();
        _lastAppliedColor = null;
        _hasAppliedOnce = false;
    }

    /// <summary>按当前配置启用或停用。开关切换时由设置界面调用。</summary>
    public void ApplyConfig()
    {
        if (!Config.WallpaperAsAccentColorSource || !OperatingSystem.IsWindows())
        {
            _timer.Stop();
            Unsubscribe();

            // 仅在用户主动关闭开关时还原；未接管过则无事发生。
            RestoreOriginalAccent();
            return;
        }

        Subscribe();
        _timer.Start();
        _ = RefreshAsync(ignoreTimestamp: true);
    }

    private void Subscribe()
    {
        if (_isSubscribed)
        {
            return;
        }

        _settingsService.SubscribeSettingsChanges();
        _settingsService.SettingsPropertyChanged += OnSettingsPropertyChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
        {
            return;
        }

        _settingsService.SettingsPropertyChanged -= OnSettingsPropertyChanged;
        _settingsService.UnsubscribeSettingsChanges();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _isSubscribed = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.Desktop)
        {
            return;
        }

        // 切换壁纸后 Windows 需要一点时间写完转码图片，延后一拍再读取。
        _ = RefreshAsync(ignoreTimestamp: true, delay: TimeSpan.FromMilliseconds(700));
    }

    private void OnSettingsPropertyChanged(object? sender, string propertyName)
    {
        // 关注用户（而非本服务）对主题色或其来源的改动。
        if (propertyName is not ("PrimaryColor" or "ColorSource"))
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            HandleExternalColorChange();
        }
        else
        {
            Dispatcher.UIThread.Post(HandleExternalColorChange);
        }
    }

    /// <summary>
    /// 用户手动修改了主题色：交还控制权，关闭开关并保留用户刚选定的颜色。
    /// </summary>
    private void HandleExternalColorChange()
    {
        if (!Config.WallpaperAsAccentColorSource)
        {
            return;
        }

        // 尚未成功接管过主题色时不做判断：此时无从区分宿主界面的瞬时写入与用户操作，
        // 且不存在“覆盖用户选色”的问题，保持开关开启即可。
        if (!_hasAppliedOnce || _lastAppliedColor is not { } applied)
        {
            return;
        }

        // 只有当前设置确实偏离了本服务写入的结果时，才认定是用户的手动改动。
        var stillMatchesOwnState =
            _settingsService.GetColorSource() == ClassIslandSettingsService.CustomColorSource &&
            _settingsService.GetPrimaryColor() == applied;

        if (stillMatchesOwnState)
        {
            return;
        }

        _timer.Stop();
        Unsubscribe();
        _lastAppliedColor = null;
        _hasAppliedOnce = false;

        // 丢弃接管前记录的快照，避免之后把用户刚选的颜色又“还原”掉。
        ClearStoredSnapshot();
        Config.WallpaperAsAccentColorSource = false;
        _configHandler.Save();

        _logger.LogInformation("检测到手动修改主题色，已自动关闭“将桌面壁纸设置为主题色来源”。");

        IAppHost.TryGetService<SystemToolsNotificationProvider>()?.ShowNotification(
            new ClassIsland.Core.Models.Notification.NotificationRequest
            {
                MaskContent = ClassIsland.Core.Models.Notification.NotificationContent.CreateTwoIconsMask(
                    "检测到手动修改主题色，已关闭“将桌面壁纸设置为主题色来源”", "\uE9BC", "")
            });
    }

    private void OnTimerTick(object? sender, EventArgs e) => _ = RefreshAsync(ignoreTimestamp: false);

    private async Task RefreshAsync(bool ignoreTimestamp, TimeSpan delay = default)
    {
        if (!await _workLock.WaitAsync(0))
        {
            // 上一轮尚未结束，跳过本次触发。
            return;
        }

        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            if (!Config.WallpaperAsAccentColorSource)
            {
                return;
            }

            // 提取失败后进入退避期，避免壁纸不可用时反复解码大图。
            if (!ignoreTimestamp &&
                _lastFailedAttemptUtc is { } failedAt &&
                DateTime.UtcNow - failedAt < FailureRetryInterval)
            {
                return;
            }

            var hasAppliedColor = _hasAppliedOnce;
            var lastWriteTime = _lastWallpaperWriteTimeUtc;
            var outcome = await Task.Run(() => LocateAndExtract(ignoreTimestamp, hasAppliedColor, lastWriteTime));

            switch (outcome)
            {
                case ExtractOutcome.Applied applied:
                    _lastFailedAttemptUtc = null;
                    _lastWallpaperWriteTimeUtc = applied.WriteTimeUtc;
                    ApplyToClassIsland(applied.Color, applied.SourcePath);
                    break;

                case ExtractOutcome.NoChange:
                    // 壁纸没变，属于正常情况，不进入退避期。
                    _lastFailedAttemptUtc = null;
                    break;

                default:
                    _lastFailedAttemptUtc = DateTime.UtcNow;
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "从壁纸提取主题色失败，将在下次重试。");
        }
        finally
        {
            _workLock.Release();
        }
    }

    /// <summary>
    /// 在后台线程读取壁纸并提取主题色。
    /// </summary>
    /// <param name="ignoreTimestamp">忽略文件时间戳，强制重新提取。</param>
    /// <param name="hasAppliedColor">此前是否已成功应用过颜色。</param>
    /// <param name="lastWriteTimeUtc">上一次成功提取时记录的壁纸文件写入时间。</param>
    private ExtractOutcome LocateAndExtract(bool ignoreTimestamp, bool hasAppliedColor, DateTime lastWriteTimeUtc)
    {
        foreach (var path in DesktopWallpaperLocator.GetCandidatePaths())
        {
            DateTime writeTime;
            try
            {
                writeTime = System.IO.File.GetLastWriteTimeUtc(path);
            }
            catch (Exception)
            {
                continue;
            }

            if (!ignoreTimestamp && writeTime <= lastWriteTimeUtc && hasAppliedColor)
            {
                // 壁纸文件未更新，当前主题色已是该壁纸的结果。
                return ExtractOutcome.NoChange.Instance;
            }

            var color = WallpaperColorExtractor.TryExtract(path);
            if (color is null)
            {
                continue;
            }

            return new ExtractOutcome.Applied(color.Value, path, writeTime);
        }

        // 候选文件全部不可用（例如动态壁纸）或都没有可提取的色调。
        return ExtractOutcome.Failed.Instance;
    }

    private void ApplyToClassIsland(Color color, string sourcePath)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!Config.WallpaperAsAccentColorSource)
            {
                return;
            }

            if (!EnsureSnapshotCaptured())
            {
                _logger.LogDebug("无法读取应用当前的强调色设置，跳过本次应用。");
                return;
            }

            // 先写颜色再切换来源：切换来源会立即触发宿主重算主题色，
            // 此时颜色已是目标值，可避免中间态闪回原色。
            var colorOk = _settingsService.SetPrimaryColor(color);
            var sourceOk = _settingsService.SetColorSource(ClassIslandSettingsService.CustomColorSource);

            // 设置不可写时不记录状态，以免之后把宿主的其他改动误判为正常。
            if (!colorOk && !sourceOk)
            {
                _logger.LogDebug("强调色设置不可用，跳过本次应用。");
                return;
            }

            _lastAppliedColor = color;
            _hasAppliedOnce = true;

            _logger.LogInformation("已根据壁纸 {Path} 将主题色设置为 {Color}。", sourcePath, color);
        });
    }

    /// <summary>
    /// 首次接管前记录原始强调色设置并持久化，供之后关闭功能时还原。
    /// </summary>
    private bool EnsureSnapshotCaptured()
    {
        if (HasStoredSnapshot())
        {
            return true;
        }

        var primaryColor = _settingsService.GetPrimaryColor();
        var colorSource = _settingsService.GetColorSource();
        if (primaryColor is null && colorSource is null)
        {
            return false;
        }

        Config.WallpaperAccentOriginalPrimaryColor = primaryColor?.ToString();
        Config.WallpaperAccentOriginalColorSource = colorSource;
        _configHandler.Save();
        return true;
    }

    private void RestoreOriginalAccent()
    {
        if (!HasStoredSnapshot())
        {
            return;
        }

        var colorSource = Config.WallpaperAccentOriginalColorSource;
        var primaryColorHex = Config.WallpaperAccentOriginalPrimaryColor;

        ClearStoredSnapshot();

        void Restore()
        {
            if (primaryColorHex is not null)
            {
                try
                {
                    _settingsService.SetPrimaryColor(Color.Parse(primaryColorHex));
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "还原原有强调色失败，颜色值：{Color}。", primaryColorHex);
                }
            }

            if (colorSource is { } source)
            {
                _settingsService.SetColorSource(source);
            }

            _logger.LogInformation("已还原应用原本的强调色设置。");
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Restore();
        }
        else
        {
            Dispatcher.UIThread.Post(Restore);
        }
    }

    private bool HasStoredSnapshot() =>
        Config.WallpaperAccentOriginalPrimaryColor is not null ||
        Config.WallpaperAccentOriginalColorSource is not null;

    private void ClearStoredSnapshot()
    {
        Config.WallpaperAccentOriginalPrimaryColor = null;
        Config.WallpaperAccentOriginalColorSource = null;
        _configHandler.Save();
    }

    /// <summary>一次提取尝试的结果。</summary>
    private abstract record ExtractOutcome
    {
        /// <summary>壁纸未变化，当前主题色已经对应该壁纸。</summary>
        public sealed record NoChange : ExtractOutcome
        {
            public static readonly NoChange Instance = new();
        }

        /// <summary>没有可用的壁纸图片或图片无可提取色调。</summary>
        public sealed record Failed : ExtractOutcome
        {
            public static readonly Failed Instance = new();
        }

        /// <summary>成功提取到颜色。</summary>
        public sealed record Applied(Color Color, string SourcePath, DateTime WriteTimeUtc) : ExtractOutcome;
    }
}

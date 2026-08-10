using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using SystemTools.ConfigHandlers;
using SystemTools.Models;
using SystemTools.Services;
using SystemTools.Shared;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SystemTools.Views;

public partial class AiChatFloatingWindow : Window
{
    private const double BottomTolerance = 12;
    private const int AdaptiveThemeRefreshStride = 8;

    private bool _isDisposed;
    private bool _isAtConversationBottom = true;
    private AiConversation? _displayedConversation;
    private readonly IDisposable _windowStateSubscription;
    private readonly MainConfigHandler _configHandler;
    private readonly MainWindowBackgroundCaptureService _backgroundCaptureService;
    private readonly DispatcherTimer _liquidGlassCaptureTimer;
    private WriteableBitmap? _liquidGlassBackdrop;
    private WriteableBitmap? _liquidGlassSpareBackdrop;
    private IDisposable? _continuousCaptureLease;
    private IDisposable? _windowCaptureExclusionLease;
    private CancellationTokenSource? _glassCaptureCancellation;
    private Task? _glassCaptureTask;
    private bool _glassCaptureErrorReported;
    private int _adaptiveThemeRefreshCount;
    private ThemeVariant? _adaptiveThemeVariant;

    public static readonly DirectProperty<AiChatFloatingWindow, bool> IsLiquidGlassContentVisibleProperty =
        AvaloniaProperty.RegisterDirect<AiChatFloatingWindow, bool>(
            nameof(IsLiquidGlassContentVisible),
            window => window._isLiquidGlassContentVisible);

    public static readonly DirectProperty<AiChatFloatingWindow, bool> IsClassicConversationSurfaceVisibleProperty =
        AvaloniaProperty.RegisterDirect<AiChatFloatingWindow, bool>(
            nameof(IsClassicConversationSurfaceVisible),
            window => window._isClassicConversationSurfaceVisible);

    private bool _isLiquidGlassContentVisible;
    private bool _isClassicConversationSurfaceVisible = true;

    /// <summary>
    /// True only after a liquid-glass backdrop is available for the current window.
    /// The message templates use this to switch between the material and classic layers.
    /// </summary>
    public bool IsLiquidGlassContentVisible
    {
        get => _isLiquidGlassContentVisible;
        private set => SetAndRaise(IsLiquidGlassContentVisibleProperty, ref _isLiquidGlassContentVisible, value);
    }

    public bool IsClassicConversationSurfaceVisible
    {
        get => _isClassicConversationSurfaceVisible;
        private set => SetAndRaise(
            IsClassicConversationSurfaceVisibleProperty,
            ref _isClassicConversationSurfaceVisible,
            value);
    }

    public LiquidGlassSettings ConversationGlassSettings => _configHandler.Data.AiConversationLiquidGlass;

    public AiChatFloatingWindow()
        : this(
            IAppHost.GetService<AiConversationStore>(),
            IAppHost.GetService<IOpenAiCompatibleService>(),
            IAppHost.GetService<AiPromptService>(),
            IAppHost.GetService<AiChatOperationGate>(),
            IAppHost.GetService<VoskSpeechService>(),
            IAppHost.GetService<MainConfigHandler>(),
            IAppHost.GetService<SystemToolsNotificationProvider>(),
            IAppHost.GetService<ClassIslandProfileAiService>(),
            IAppHost.GetService<ClassIslandActionAiService>(),
            IAppHost.GetService<MainWindowBackgroundCaptureService>())
    {
    }

    public AiChatFloatingWindow(
        AiConversationStore store,
        IOpenAiCompatibleService aiService,
        AiPromptService promptService,
        AiChatOperationGate operationGate,
        VoskSpeechService speechService,
        MainConfigHandler configHandler,
        SystemToolsNotificationProvider notificationProvider,
        ClassIslandProfileAiService profileAiService,
        ClassIslandActionAiService actionAiService,
        MainWindowBackgroundCaptureService backgroundCaptureService)
    {
        _configHandler = configHandler;
        _backgroundCaptureService = backgroundCaptureService;
        ViewModel = new AiChatSettingsViewModel(
            store,
            aiService,
            promptService,
            operationGate,
            speechService,
            configHandler,
            notificationProvider,
            profileAiService,
            actionAiService,
            ConfirmProfileModificationAsync,
            ConfirmActionExecutionAsync);
        DataContext = ViewModel;
        InitializeComponent();
        RequestedThemeVariant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        _liquidGlassCaptureTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(_configHandler.Data.AiConversationLiquidGlass.BackdropRefreshIntervalMs),
            DispatcherPriority.Background,
            (_, _) => QueueLiquidGlassBackdropCapture());

        _displayedConversation = ViewModel.SelectedConversation;
        ViewModel.ConversationContentChanged += ViewModel_OnConversationContentChanged;
        _configHandler.Data.PropertyChanged += Config_OnPropertyChanged;
        Opened += Window_OnOpened;
        PositionChanged += Window_OnPositionChanged;
        ApplyLiquidGlassAppearance();
        _windowStateSubscription = this.GetPropertyChangedObservable(WindowStateProperty).Subscribe(_ =>
        {
            if (WindowState == WindowState.Normal)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                WindowState = WindowState.Normal;
                Activate();
            }, DispatcherPriority.MaxValue);
        });
    }

    public AiChatSettingsViewModel ViewModel { get; }

    public void BringToFront()
    {
        Topmost = true;
        WindowState = WindowState.Normal;
        if (!IsVisible)
        {
            Show();
        }
        Activate();
    }

    private void Window_OnOpened(object? sender, EventArgs e) => UpdateLiquidGlassCaptureLoop();

    private void Window_OnPositionChanged(object? sender, PixelPointEventArgs e)
        => UpdateLiquidGlassCaptureLoop();

    private void Config_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(MainConfigData.AiConversationFloatingWindowStyle) and
            not nameof(MainConfigData.AiConversationLiquidGlass))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _adaptiveThemeRefreshCount = 0;
            ApplyLiquidGlassAppearance();
            UpdateLiquidGlassCaptureLoop();
        });
    }

    private void UpdateLiquidGlassCaptureLoop()
    {
        _liquidGlassCaptureTimer.Interval = TimeSpan.FromMilliseconds(
            _configHandler.Data.AiConversationLiquidGlass.BackdropRefreshIntervalMs);
        var shouldCapture = !_isDisposed &&
                            IsVisible;
        if (shouldCapture)
        {
            _continuousCaptureLease ??= _backgroundCaptureService.BeginContinuousCapture();
            if (_windowCaptureExclusionLease is null)
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                _windowCaptureExclusionLease = _backgroundCaptureService.BeginExcludedWindowCapture(handle);
            }

            if (!_liquidGlassCaptureTimer.IsEnabled)
            {
                _liquidGlassCaptureTimer.Start();
            }

            QueueLiquidGlassBackdropCapture();
            return;
        }

        _liquidGlassCaptureTimer.Stop();
        _glassCaptureCancellation?.Cancel();
        if (_glassCaptureTask is null)
        {
            ReleaseLiquidGlassBackdrops();
        }
    }

    private void QueueLiquidGlassBackdropCapture()
    {
        if (_isDisposed || !IsVisible ||
            !OperatingSystem.IsWindows())
        {
            return;
        }

        if (_glassCaptureTask is not null)
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var scaling = Math.Max(0.1, RenderScaling);
        var width = Math.Max(1, (int)Math.Ceiling(ClientSize.Width * scaling));
        var height = Math.Max(1, (int)Math.Ceiling(ClientSize.Height * scaling));
        var area = new DrawingRectangle(Position.X, Position.Y, width, height);
        var cancellation = new CancellationTokenSource();
        _glassCaptureCancellation = cancellation;
        _glassCaptureTask = CaptureLiquidGlassBackdropAsync(handle, area, cancellation.Token);
        var captureTask = _glassCaptureTask;
        _ = captureTask.ContinueWith(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_glassCaptureTask, captureTask))
                {
                    return;
                }

                _glassCaptureTask = null;
                _glassCaptureCancellation?.Dispose();
                _glassCaptureCancellation = null;
                if (_isDisposed || !IsVisible)
                {
                    ReleaseLiquidGlassBackdrops();
                }
            }),
            TaskScheduler.Default);
    }

    private async Task CaptureLiquidGlassBackdropAsync(
        IntPtr windowHandle,
        DrawingRectangle area,
        CancellationToken cancellationToken)
    {
        try
        {
            using var frame = await Task.Run(
                () => _backgroundCaptureService.CaptureAreaAsync(
                    area,
                    windowHandle,
                    cancellationToken),
                cancellationToken);
            if (frame is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _glassCaptureErrorReported = false;
                UpdateAdaptiveTheme(frame);

                if (_configHandler.Data.AiConversationFloatingWindowStyle != 1)
                {
                    ReleaseLiquidGlassBackdropImages();
                    ApplyLiquidGlassAppearance();
                    return;
                }

                var bitmap = LiquidGlassBackdropFactory.Update(frame, _liquidGlassSpareBackdrop);
                if (bitmap is null)
                {
                    return;
                }

                var previous = _liquidGlassBackdrop;
                _liquidGlassBackdrop = bitmap;
                _liquidGlassSpareBackdrop = previous;
                LiquidGlassBackdropImage.Source = bitmap;
                ApplyLiquidGlassAppearance();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_glassCaptureErrorReported)
            {
                _glassCaptureErrorReported = true;
                ViewModel.ReportError($"背景捕获失败：{ex.Message}");
            }
        }
    }

    private void ReleaseLiquidGlassBackdrops()
    {
        _windowCaptureExclusionLease?.Dispose();
        _windowCaptureExclusionLease = null;
        _continuousCaptureLease?.Dispose();
        _continuousCaptureLease = null;
        LiquidGlassBackdropImage.Source = null;
        _liquidGlassBackdrop?.Dispose();
        _liquidGlassBackdrop = null;
        _liquidGlassSpareBackdrop?.Dispose();
        _liquidGlassSpareBackdrop = null;
    }

    private void ReleaseLiquidGlassBackdropImages()
    {
        LiquidGlassBackdropImage.Source = null;
        _liquidGlassBackdrop?.Dispose();
        _liquidGlassBackdrop = null;
        _liquidGlassSpareBackdrop?.Dispose();
        _liquidGlassSpareBackdrop = null;
    }

    private void ApplyLiquidGlassAppearance()
    {
        var settings = _configHandler.Data.AiConversationLiquidGlass;
        var useLiquidGlass = _configHandler.Data.AiConversationFloatingWindowStyle == 1 &&
                             _liquidGlassBackdrop is not null;
        IsLiquidGlassContentVisible = useLiquidGlass;
        IsClassicConversationSurfaceVisible = !useLiquidGlass;
        var cornerRadius = new CornerRadius(settings.CornerRadius);
        LiquidGlassBackdropClip.CornerRadius = cornerRadius;
        LiquidGlassSurface.CornerRadius = cornerRadius;
        LiquidGlassBackdropClip.IsVisible = useLiquidGlass;
        LiquidGlassSurface.IsVisible = useLiquidGlass;
        FrostedBackgroundBorder.IsVisible = !useLiquidGlass;
        RootBorder.CornerRadius = useLiquidGlass ? cornerRadius : new CornerRadius(12);

        LiquidGlassSurface.BackdropZoom = settings.BackdropZoom;
        LiquidGlassSurface.BackdropOffset = new Vector(settings.BackdropOffsetX, settings.BackdropOffsetY);
        LiquidGlassSurface.RefractionHeight = settings.RefractionHeight;
        LiquidGlassSurface.RefractionAmount = settings.RefractionAmount;
        LiquidGlassSurface.DepthEffect = settings.DepthEffect;
        LiquidGlassSurface.ChromaticAberration = settings.ChromaticAberration;
        LiquidGlassSurface.BlurRadius = settings.BlurRadius;
        LiquidGlassSurface.Vibrancy = settings.Vibrancy;
        LiquidGlassSurface.Brightness = settings.Brightness;
        LiquidGlassSurface.Contrast = settings.Contrast;
        LiquidGlassSurface.ExposureEv = settings.ExposureEv;
        LiquidGlassSurface.GammaPower = settings.GammaPower;
        LiquidGlassSurface.BackdropOpacity = settings.BackdropOpacity;
        LiquidGlassSurface.TintColor = ParseColor(settings.TintColor, Colors.Transparent);
        LiquidGlassSurface.SurfaceColor = ParseColor(settings.SurfaceColor, Colors.Transparent);
        LiquidGlassSurface.ProgressiveBlurEnabled = settings.ProgressiveBlurEnabled;
        LiquidGlassSurface.ProgressiveBlurStart = settings.ProgressiveBlurStart;
        LiquidGlassSurface.ProgressiveBlurEnd = settings.ProgressiveBlurEnd;
        LiquidGlassSurface.ProgressiveTintColor = ParseColor(settings.ProgressiveTintColor, Colors.Transparent);
        LiquidGlassSurface.ProgressiveTintIntensity = settings.ProgressiveTintIntensity;
        LiquidGlassSurface.AdaptiveLuminanceEnabled = settings.AdaptiveLuminanceEnabled;
        LiquidGlassSurface.AdaptiveLuminanceUpdateIntervalMs = settings.AdaptiveLuminanceUpdateIntervalMs;
        LiquidGlassSurface.AdaptiveLuminanceSmoothing = settings.AdaptiveLuminanceSmoothing;
        LiquidGlassSurface.HighlightEnabled = settings.HighlightEnabled;
        LiquidGlassSurface.HighlightWidth = settings.HighlightWidth;
        LiquidGlassSurface.HighlightBlurRadius = settings.HighlightBlurRadius;
        LiquidGlassSurface.HighlightOpacity = settings.HighlightOpacity;
        LiquidGlassSurface.HighlightAngle = settings.HighlightAngle;
        LiquidGlassSurface.HighlightFalloff = settings.HighlightFalloff;
        LiquidGlassSurface.ShadowEnabled = settings.ShadowEnabled;
        LiquidGlassSurface.ShadowRadius = settings.ShadowRadius;
        LiquidGlassSurface.ShadowOffset = new Vector(settings.ShadowOffsetX, settings.ShadowOffsetY);
        LiquidGlassSurface.ShadowColor = ParseColor(settings.ShadowColor, Color.FromArgb(26, 0, 0, 0));
        LiquidGlassSurface.ShadowOpacity = settings.ShadowOpacity;
        LiquidGlassSurface.InnerShadowEnabled = settings.InnerShadowEnabled;
        LiquidGlassSurface.InnerShadowRadius = settings.InnerShadowRadius;
        LiquidGlassSurface.InnerShadowOffset = new Vector(settings.InnerShadowOffsetX, settings.InnerShadowOffsetY);
        LiquidGlassSurface.InnerShadowColor = ParseColor(settings.InnerShadowColor, Color.FromArgb(38, 0, 0, 0));
        LiquidGlassSurface.InnerShadowOpacity = settings.InnerShadowOpacity;
    }

    private void UpdateAdaptiveTheme(MainWindowBackgroundFrame frame)
    {
        _adaptiveThemeRefreshCount++;
        if (_adaptiveThemeRefreshCount < AdaptiveThemeRefreshStride)
        {
            return;
        }

        _adaptiveThemeRefreshCount = 0;
        var luminance = BackgroundLuminanceCalculator.CalculateAverage(frame);
        if (luminance is null)
        {
            return;
        }

        var nextTheme = luminance < BackgroundLuminanceCalculator.DarkThreshold
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        if (Equals(_adaptiveThemeVariant, nextTheme))
        {
            return;
        }

        _adaptiveThemeVariant = nextTheme;
        RequestedThemeVariant = nextTheme;
        ApplyLiquidGlassAppearance();
    }

    private static Color ParseColor(string value, Color fallback) =>
        Color.TryParse(value, out var color) ? color : fallback;

    private async void SendButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void VoiceInputButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsVoiceInputActive &&
            !await SpeechRecognitionDependencyPrompt.EnsureAvailableAsync(this))
        {
            return;
        }

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
            var files = await StorageProvider.OpenFilePickerAsync(
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
            var clipboard = Clipboard;
            if (clipboard is null)
            {
                return false;
            }

            // Text keeps the TextBox's normal paste behavior even if another format is also present.
            if (await clipboard.TryGetTextAsync() is not null)
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
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ViewModel.AddPendingAttachments([attachment!]);
                    ViewModel.ReportError(string.Empty);
                });
                return true;
            }

            await Dispatcher.UIThread.InvokeAsync(() => ViewModel.ReportError(error!));
            return true;
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ViewModel.ReportError($"无法粘贴图片：{ex.Message}"));
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

    private void ChatWindow_OnDragEnter(object? sender, DragEventArgs e)
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

    private void ChatWindow_OnDragLeave(object? sender, DragEventArgs e)
    {
        AttachmentDropOverlay.Hide();
    }

    private async void ChatWindow_OnDrop(object? sender, DragEventArgs e)
    {
        AttachmentDropOverlay.Hide();
        var files = GetDroppedFiles(e);
        if (files.Count == 0 || !ViewModel.TryBeginAttachmentUpdate())
        {
            return;
        }

        try
        {
            var result = await AiAttachmentDropService.LoadAndConfirmAsync(
                this,
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
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                            ?? throw new InvalidOperationException("无法访问系统剪贴板");
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

    private void WindowTitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            e.Source is not Button &&
            (e.Source as Visual)?.FindAncestorOfType<Button>() is null)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void NewConversationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.CreateNewConversation();
        ScrollToConversationBottom();
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

    protected override void OnClosed(EventArgs e)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _configHandler.Data.PropertyChanged -= Config_OnPropertyChanged;
            Opened -= Window_OnOpened;
            PositionChanged -= Window_OnPositionChanged;
            _liquidGlassCaptureTimer.Stop();
            _glassCaptureCancellation?.Cancel();
            if (_glassCaptureTask is null)
            {
                _glassCaptureCancellation?.Dispose();
                _glassCaptureCancellation = null;
                ReleaseLiquidGlassBackdrops();
            }
            ViewModel.ConversationContentChanged -= ViewModel_OnConversationContentChanged;
            ViewModel.StopGeneration();
            ViewModel.Dispose();
            _windowStateSubscription.Dispose();
        }

        base.OnClosed(e);
    }
}

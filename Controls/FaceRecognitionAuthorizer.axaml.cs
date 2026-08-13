using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ClassIsland.Core.Attributes;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.Controls;

[AuthorizeProviderInfo("systemtools.authProviders.faceRecognition", "人脸识别", "\uED1B")]
public partial class FaceRecognitionAuthorizer : AuthorizeProviderControlBase<FaceRecognitionSettings>, IDisposable
{
    private FaceRecognitionService? _faceService;
    private CameraCaptureService? _cameraService;
    private WriteableBitmap? _bitmap;
    private readonly object _frameLock = new();
    private readonly object _resourceLock = new();
    private Mat? _currentFrame;
    private int _isDrawing;
    private int _isActive;
    private int _lifecycleGeneration;
    private int _authorizationCompleted;

    private readonly SemaphoreSlim _verifySemaphore = new(1, 1);
    private CancellationTokenSource? _verifyCts;

    public FaceRecognitionAuthorizer()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        int generation;
        lock (_resourceLock)
        {
            if (_faceService != null || _cameraService != null || Volatile.Read(ref _isActive) != 0)
            {
                return;
            }

            generation = ++_lifecycleGeneration;
            Interlocked.Exchange(ref _isActive, 1);
            Interlocked.Exchange(ref _authorizationCompleted, 0);
        }

        Settings.Operating = true;
        Settings.OperationFinished = false;
        SetStatus("正在准备人脸识别…");
        Settings.CameraReady = false;
        Settings.CameraPlaceholderText = "正在准备摄像头画面";
        UpdateCaptureButtonText();
        var serviceReady = false;

        try
        {
            var initializedService = await Task.Run(() =>
            {
                var service = new FaceRecognitionService(DependencyPaths.GetDependencyRoot());
                if (!service.Initialize())
                {
                    service.Dispose();
                    return null;
                }

                return service;
            });

            if (!IsCurrentGeneration(generation))
            {
                initializedService?.Dispose();
                return;
            }

            if (initializedService == null)
            {
                Settings.CameraPlaceholderText = "人脸识别不可用";
                SetStatus("人脸识别模型初始化失败，请检查依赖文件。", true);
                return;
            }

            lock (_resourceLock)
            {
                if (Volatile.Read(ref _isActive) == 0 || _lifecycleGeneration != generation || _faceService != null)
                {
                    initializedService.Dispose();
                    return;
                }

                _faceService = initializedService;
            }
            serviceReady = true;

            var cameraStarted = await Task.Run(() => StartCamera(generation));
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            if (!cameraStarted)
            {
                Settings.OperationFinished = true;
                Settings.CameraPlaceholderText = "摄像头不可用";
                SetStatus("无法启动摄像头，请检查设备权限或摄像头占用。", true, true);
            }
            else
            {
                SetStatus(IsEditingMode
                    ? "请将面部置于取景框中央，然后捕获人脸。"
                    : "请面对摄像头，系统会自动连续验证。");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"初始化崩溃: {ex.Message}");
            if (IsCurrentGeneration(generation))
            {
                Settings.OperationFinished = true;
                Settings.CameraPlaceholderText = "人脸识别不可用";
                SetStatus("人脸识别初始化失败，请稍后重试。", true);
            }
        }
        finally
        {
            FaceRecognitionService? failedService = null;
            var ownsGeneration = false;
            lock (_resourceLock)
            {
                ownsGeneration = _lifecycleGeneration == generation && Volatile.Read(ref _isActive) != 0;
                if (ownsGeneration && !serviceReady)
                {
                    Interlocked.Exchange(ref _isActive, 0);
                    failedService = _faceService;
                    _faceService = null;
                }
            }

            failedService?.Dispose();
            if (ownsGeneration && !serviceReady)
            {
                Settings.OperationFinished = true;
            }

            if (ownsGeneration)
            {
                Settings.Operating = false;
            }
        }
    }

    private bool IsCurrentGeneration(int generation)
    {
        lock (_resourceLock)
        {
            return _lifecycleGeneration == generation && Volatile.Read(ref _isActive) != 0;
        }
    }

    private bool StartCamera(int generation)
    {
        var cameraService = new CameraCaptureService();
        cameraService.FrameCaptured += OnFrameCaptured;

        if (!cameraService.Start(0, 640, 480))
        {
            cameraService.FrameCaptured -= OnFrameCaptured;
            cameraService.Dispose();
            return false;
        }

        var accepted = false;
        lock (_resourceLock)
        {
            if (Volatile.Read(ref _isActive) != 0 &&
                _lifecycleGeneration == generation &&
                _cameraService == null)
            {
                _cameraService = cameraService;
                accepted = true;
            }
        }

        if (accepted)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsCurrentGeneration(generation))
                {
                    Settings.CameraReady = true;
                    UpdateCaptureButtonText();
                }
            });
            return true;
        }

        cameraService.FrameCaptured -= OnFrameCaptured;
        cameraService.Dispose();
        return false;
    }

    private void OnFrameCaptured(object? sender, Mat frame)
    {
        FaceRecognitionService? faceService;
        int generation;
        lock (_resourceLock)
        {
            if (Volatile.Read(ref _isActive) == 0 || !ReferenceEquals(sender, _cameraService))
            {
                frame.Dispose();
                return;
            }

            faceService = _faceService;
            generation = _lifecycleGeneration;
        }

        if (faceService == null)
        {
            frame.Dispose();
            return;
        }

        Mat? oldFrame;
        lock (_frameLock)
        {
            oldFrame = _currentFrame;
            _currentFrame = frame;
        }
        oldFrame?.Dispose();

        if (Interlocked.CompareExchange(ref _isDrawing, 1, 0) != 0) return;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (!IsCurrentGeneration(generation))
                {
                    return;
                }

                using var previewFrame = CloneCurrentFrame();
                if (!IsCurrentGeneration(generation) || previewFrame == null || previewFrame.Empty()) return;
                UpdatePreview(previewFrame);

                if (!IsEditingMode &&
                    !string.IsNullOrEmpty(Settings.FaceTemplate) &&
                    Volatile.Read(ref _authorizationCompleted) == 0 &&
                    IsCurrentGeneration(generation) &&
                    _verifySemaphore.Wait(0))
                {
                    CancellationToken token;
                    lock (_resourceLock)
                    {
                        if (_verifyCts == null || _verifyCts.IsCancellationRequested)
                        {
                            _verifyCts?.Dispose();
                            _verifyCts = new CancellationTokenSource();
                        }
                        token = _verifyCts.Token;
                    }

                    var processMat = previewFrame.Clone();
                    _ = DoVerifyAsync(processMat, token, faceService, generation);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isDrawing, 0);
            }
        });
    }

    private Mat? CloneCurrentFrame()
    {
        lock (_frameLock)
        {
            if (_currentFrame == null || _currentFrame.Empty())
            {
                return null;
            }

            return _currentFrame.Clone();
        }
    }

    private void UpdatePreview(Mat frame)
    {
        if (_bitmap == null ||
            _bitmap.PixelSize.Width != frame.Width ||
            _bitmap.PixelSize.Height != frame.Height)
        {
            var newBitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            CameraPreview.Source = null;
            var oldBitmap = _bitmap;
            _bitmap = newBitmap;
            oldBitmap?.Dispose();
        }

        using var bgraMat = new Mat();
        Cv2.CvtColor(frame, bgraMat, ColorConversionCodes.BGR2BGRA);
        using var locked = _bitmap.Lock();
        unsafe
        {
            var src = (byte*)bgraMat.Data.ToPointer();
            var dst = (byte*)locked.Address.ToPointer();
            for (int i = 0; i < frame.Height; i++)
            {
                Buffer.MemoryCopy(src + i * bgraMat.Step(), dst + i * locked.RowBytes, locked.RowBytes, frame.Width * 4);
            }
        }
        CameraPreview.Source = _bitmap;
    }

    private async void OnCaptureClick(object? sender, RoutedEventArgs e)
    {
        CameraCaptureService? cameraService;
        FaceRecognitionService? faceService;
        int generation;
        lock (_resourceLock)
        {
            cameraService = _cameraService;
            faceService = _faceService;
            generation = _lifecycleGeneration;
        }

        if (!IsCurrentGeneration(generation))
        {
            return;
        }

        if (cameraService == null || !cameraService.IsRunning)
        {
            ShutdownCamera();
            Settings.Operating = true;
            Settings.OperationFinished = false;
            SetStatus("正在启动摄像头…");
            Settings.CameraPlaceholderText = "正在启动摄像头";
            var started = await Task.Run(() => StartCamera(generation));
            if (IsCurrentGeneration(generation))
            {
                Settings.OperationFinished = !started;
                Settings.Operating = false;
                SetStatus(started
                    ? "摄像头已就绪，请将面部置于取景框中央。"
                    : "无法启动摄像头，请检查设备权限或摄像头占用。",
                    !started, !started);
                if (!started)
                {
                    Settings.CameraPlaceholderText = "摄像头不可用";
                }
            }
            return;
        }

        if (faceService == null)
            return;

        using var snapshot = CloneCurrentFrame();
        if (snapshot == null || snapshot.Empty())
        {
            Settings.OperationFinished = true;
            SetStatus("尚未获取到摄像头画面，请稍后重试。", true);
            return;
        }

        _verifyCts?.Cancel();
        _verifyCts?.Dispose();
        _verifyCts = null;

        Settings.Operating = true;
        Settings.OperationFinished = false;
        SetStatus("正在提取人脸特征…");

        try
        {
            var encoding = await Task.Run(() =>
            {
                try
                {
                    byte[] rgbBytes = MatToRgbBytes(snapshot);
                    return faceService.ExtractFaceEncoding(rgbBytes, snapshot.Width, snapshot.Height);
                }
                catch { return null; }
            });

            if (encoding != null && IsCurrentGeneration(generation))
            {
                Settings.FaceTemplate = faceService.EncodeToString(encoding);
                ShutdownCamera();
                Settings.CameraPlaceholderText = "人脸信息已保存";
                SetStatus("人脸信息已保存。再次录入时会替换当前信息。");
            }
            else
            {
                if (IsCurrentGeneration(generation))
                {
                    Settings.OperationFinished = true;
                    SetStatus("未检测到清晰人脸，请调整距离、角度或光线后重试。", true);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"捕获流程崩溃: {ex.Message}");
            if (IsCurrentGeneration(generation))
            {
                Settings.OperationFinished = true;
                SetStatus("录入人脸时发生错误，请重试。", true);
            }
        }
        finally
        {
            if (IsCurrentGeneration(generation))
            {
                Settings.Operating = false;
            }
        }
    }

    private async Task DoVerifyAsync(Mat mat, CancellationToken cancellationToken,
        FaceRecognitionService faceService, int generation)
    {
        var updateOperatingState = false;
        try
        {
            using (mat)
            {
                if (!IsCurrentGeneration(generation) ||
                    Volatile.Read(ref _authorizationCompleted) != 0 ||
                    string.IsNullOrEmpty(Settings.FaceTemplate))
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation) && Volatile.Read(ref _authorizationCompleted) == 0)
                    {
                        updateOperatingState = true;
                        Settings.Operating = true;
                        Settings.OperationFinished = false;
                    }
                });

                cancellationToken.ThrowIfCancellationRequested();

                var target = faceService.DecodeFromString(Settings.FaceTemplate);
                if (target == null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsCurrentGeneration(generation))
                        {
                            Settings.OperationFinished = true;
                            SetStatus("已录入的人脸数据无效，请重新录入。", true);
                        }
                    });
                    return;
                }

                var current = await Task.Run(() =>
                    faceService.ExtractFaceEncoding(MatToRgbBytes(mat), mat.Width, mat.Height),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentGeneration(generation) || Volatile.Read(ref _authorizationCompleted) != 0)
                {
                    return;
                }

                if (current != null)
                {
                    var dist = faceService.ComputeDistance(target, current);
                    if (dist < Settings.Threshold)
                    {
                        if (Interlocked.CompareExchange(ref _authorizationCompleted, 1, 0) == 0)
                        {
                            var authorizeCommandExecuted = await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (!IsCurrentGeneration(generation))
                                {
                                    return false;
                                }

                                // CompleteAuthorize() does not specify a routed-command target.
                                // Automatic recognition has no focused element, so explicitly
                                // start routing from this control to reach AuthorizeWindow.
                                if (AuthorizeProviderControlBase.CompleteAuthorizeCommand is not RoutedCommand command ||
                                    !command.CanExecute(null, this))
                                {
                                    return false;
                                }

                                command.Execute(null, this);
                                return true;
                            });

                            if (!authorizeCommandExecuted)
                            {
                                Interlocked.Exchange(ref _authorizationCompleted, 0);
                            }
                        }
                        return;
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation) && Volatile.Read(ref _authorizationCompleted) == 0)
                    {
                        SetStatus("未识别到清晰人脸，正在继续尝试…");
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"验证异常: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsCurrentGeneration(generation) && Volatile.Read(ref _authorizationCompleted) == 0)
                {
                    SetStatus("本轮验证遇到错误，正在自动重试…");
                }
            });
        }
        finally
        {
            if (updateOperatingState)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation))
                    {
                        Settings.Operating = false;
                    }
                });
            }
            _verifySemaphore.Release();
        }
    }

    private async void OnManualVerifyClick(object? sender, RoutedEventArgs e)
    {
        Settings.OperationFinished = false;
        SetStatus("正在重新启动摄像头…");
        Settings.CameraPlaceholderText = "正在重新启动摄像头";

        int generation;
        CameraCaptureService? cameraService;
        lock (_resourceLock)
        {
            generation = _lifecycleGeneration;
            cameraService = _cameraService;
        }

        if (!IsCurrentGeneration(generation))
        {
            return;
        }

        if (cameraService is { IsRunning: true })
        {
            SetStatus("请面对摄像头，系统会自动连续验证。");
            return;
        }

        ShutdownCamera();
        Settings.Operating = true;
        var started = await Task.Run(() => StartCamera(generation));
        if (IsCurrentGeneration(generation))
        {
            Settings.OperationFinished = !started;
            Settings.Operating = false;
            SetStatus(started
                ? "摄像头已恢复，正在自动连续验证。"
                : "仍无法启动摄像头，请检查设备权限或摄像头占用。",
                !started, !started);
            if (!started)
            {
                Settings.CameraPlaceholderText = "摄像头不可用";
            }
        }
    }

    private void SetStatus(string message, bool isError = false, bool canRetry = false)
    {
        Settings.StatusMessage = message;
        Settings.HasError = isError;
        Settings.CanRetry = canRetry;
    }

    private void UpdateCaptureButtonText()
    {
        Settings.CaptureButtonText = Settings.CameraReady
            ? string.IsNullOrEmpty(Settings.FaceTemplate)
                ? "捕获并保存人脸"
                : "捕获并更新人脸"
            : "重新打开摄像头";
    }

    private byte[] MatToRgbBytes(Mat mat)
    {
        using var rgb = new Mat();
        Cv2.CvtColor(mat, rgb, ColorConversionCodes.BGR2RGB);
        byte[] buf = new byte[mat.Width * mat.Height * 3];
        Marshal.Copy(rgb.Data, buf, 0, buf.Length);
        return buf;
    }

    public override bool ValidateAuthorizeSettings() => !string.IsNullOrEmpty(Settings.FaceTemplate);

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        lock (_resourceLock)
        {
            Interlocked.Exchange(ref _isActive, 0);
            ++_lifecycleGeneration;
        }

        var verifyCts = Interlocked.Exchange(ref _verifyCts, null);
        verifyCts?.Cancel();
        verifyCts?.Dispose();

        ShutdownCamera();
        FaceRecognitionService? faceService;
        lock (_resourceLock)
        {
            faceService = _faceService;
            _faceService = null;
        }
        faceService?.Dispose();

        Mat? frame;
        lock (_frameLock)
        {
            frame = _currentFrame;
            _currentFrame = null;
        }
        frame?.Dispose();
        CameraPreview.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        Settings.CameraReady = false;
        Settings.CameraPlaceholderText = "摄像头已停止";
        UpdateCaptureButtonText();
        base.OnUnloaded(e);
    }

    private void ShutdownCamera()
    {
        CameraCaptureService? cameraService;
        lock (_resourceLock)
        {
            cameraService = _cameraService;
            _cameraService = null;
        }

        if (cameraService != null)
        {
            cameraService.FrameCaptured -= OnFrameCaptured;
            cameraService.Stop();
            cameraService.Dispose();
        }

        Settings.CameraReady = false;
        UpdateCaptureButtonText();
    }

    public void Dispose()
    {
        lock (_resourceLock)
        {
            Interlocked.Exchange(ref _isActive, 0);
            ++_lifecycleGeneration;
        }
        var verifyCts = Interlocked.Exchange(ref _verifyCts, null);
        verifyCts?.Cancel();
        verifyCts?.Dispose();

        ShutdownCamera();

        Mat? frame;
        lock (_frameLock)
        {
            frame = _currentFrame;
            _currentFrame = null;
        }
        frame?.Dispose();

        _bitmap?.Dispose();
        _bitmap = null;
        Settings.CameraReady = false;

        FaceRecognitionService? faceService;
        lock (_resourceLock)
        {
            faceService = _faceService;
            _faceService = null;
        }
        faceService?.Dispose();
        GC.SuppressFinalize(this);
    }
}

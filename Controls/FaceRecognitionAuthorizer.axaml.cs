using Avalonia.Interactivity;
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
    private Mat? _currentFrame;
    private bool _isDrawing = false;
    private DateTime _lastAuthTime = DateTime.MinValue;

    private readonly SemaphoreSlim _verifySemaphore = new(1, 1);
    private CancellationTokenSource? _verifyCts;

    public FaceRecognitionAuthorizer()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (_faceService != null) return;

        Settings.Operating = true;
        Settings.OperationFinished = false;

        try
        {
            bool initSuccess = await Task.Run(() =>
            {
                _faceService = new FaceRecognitionService(GlobalConstants.Information.PluginFolder);
                return _faceService.Initialize();
            });

            if (!initSuccess)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Settings.OperationFinished = true;
                });
                return;
            }

            await Task.Run(() =>
            {
                _cameraService = new CameraCaptureService();
                _cameraService.FrameCaptured += OnFrameCaptured;
                _cameraService.Start(0, 640, 480);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"初始化崩溃: {ex.Message}");
        }
        finally
        {
            Settings.Operating = false;
        }
    }

    private void StartCamera()
    {
        _cameraService = new CameraCaptureService();
        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.Start(0, 640, 480);
    }

    private void OnFrameCaptured(object? sender, Mat frame)
    {
        var oldFrame = _currentFrame;
        _currentFrame = frame;
        oldFrame?.Dispose();

        if (_isDrawing) return;
        _isDrawing = true;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (_currentFrame == null || _currentFrame.Empty()) return;
                UpdatePreview(_currentFrame);

                if (!IsEditingMode && !string.IsNullOrEmpty(Settings.FaceTemplate))
                {
                    if ((DateTime.Now - _lastAuthTime).TotalMilliseconds > 1000)
                    {
                        _lastAuthTime = DateTime.Now;

                        _verifyCts?.Cancel();
                        _verifyCts?.Dispose();
                        _verifyCts = new CancellationTokenSource();
                        var token = _verifyCts.Token;

                        var processMat = _currentFrame.Clone();
                        _ = DoVerifyAsync(processMat, token);
                    }
                }
            }
            finally
            {
                _isDrawing = false;
            }
        });
    }

    private void UpdatePreview(Mat frame)
    {
        if (_bitmap == null || _bitmap.PixelSize.Width != frame.Width)
        {
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
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
        if (_cameraService == null)
        {
            StartCamera();
            Settings.FaceTemplate = null;
            return;
        }

        if (_currentFrame == null || _currentFrame.Empty() || _faceService == null)
            return;

        _verifyCts?.Cancel();
        _verifyCts?.Dispose();
        _verifyCts = null;

        Settings.Operating = true;
        Settings.OperationFinished = false;

        try
        {
            using var snapshot = _currentFrame.Clone();
            var encoding = await Task.Run(() =>
            {
                try
                {
                    byte[] rgbBytes = MatToRgbBytes(snapshot);
                    return _faceService.ExtractFaceEncoding(rgbBytes, snapshot.Width, snapshot.Height);
                }
                catch { return null; }
            });

            if (encoding != null)
            {
                Settings.FaceTemplate = _faceService.EncodeToString(encoding);
                ShutdownCamera();
            }
            else
            {
                Settings.OperationFinished = true;
                await Task.Delay(3000);
                Settings.OperationFinished = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"捕获流程崩溃: {ex.Message}");
        }
        finally
        {
            Settings.Operating = false;
        }
    }

    private async Task DoVerifyAsync(Mat mat, CancellationToken cancellationToken)
    {
        using (mat)
        {
            if (_faceService == null || string.IsNullOrEmpty(Settings.FaceTemplate))
                return;

            await _verifySemaphore.WaitAsync(cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() => Settings.Operating = true);

            try
            {
                var target = _faceService.DecodeFromString(Settings.FaceTemplate);

                var current = await Task.Run(() =>
                    _faceService.ExtractFaceEncoding(MatToRgbBytes(mat), mat.Width, mat.Height),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (target != null && current != null)
                {
                    var dist = _faceService.ComputeDistance(target, current);
                    if (dist < Settings.Threshold)
                    {
                        await Dispatcher.UIThread.InvokeAsync(CompleteAuthorize);
                        return;
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() => Settings.OperationFinished = true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"验证异常: {ex.Message}");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => Settings.Operating = false);
                _verifySemaphore.Release();
            }
        }
    }

    private void OnManualVerifyClick(object? sender, RoutedEventArgs e)
    {
        Settings.OperationFinished = false;
        _lastAuthTime = DateTime.MinValue;
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
        _verifyCts?.Cancel();
        _verifyCts?.Dispose();

        ShutdownCamera();
        base.OnUnloaded(e);
        _cameraService?.Dispose();
        _faceService?.Dispose();
        _currentFrame?.Dispose();
    }

    private void ShutdownCamera()
    {
        if (_cameraService != null)
        {
            _cameraService.FrameCaptured -= OnFrameCaptured;
            _cameraService.Stop();
            _cameraService.Dispose();
            _cameraService = null;
        }
    }

    public void Dispose()
    {
        _verifyCts?.Dispose();
        _verifySemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}
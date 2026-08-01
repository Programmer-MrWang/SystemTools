using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SystemTools.ConfigHandlers;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SystemTools.Services;

public sealed class MainWindowTextOcclusionService(
    MainConfigHandler configHandler,
    MainWindowBackgroundCaptureService backgroundCaptureService,
    ClassIslandSettingsService classIslandSettingsService,
    ILogger<MainWindowTextOcclusionService> logger)
{
    private const int MinimumTextCharacterCount = 4;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _recognitionLock = new(1, 1);
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private OcrEngine? _ocrEngine;
    private bool _hiddenByThisService;
    private IDisposable? _continuousCaptureLease;
    private int _suspensionCount;
    private bool _isShuttingDown;

    public void Start()
    {
        lock (_stateLock)
        {
            _isShuttingDown = false;
        }

        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        ApplyConfig();
    }

    public IDisposable Suspend()
    {
        var shouldStop = false;
        lock (_stateLock)
        {
            if (!_isShuttingDown)
            {
                shouldStop = _suspensionCount++ == 0;
            }
        }

        if (shouldStop)
        {
            Stop(restoreMainWindow: false);
        }

        return new SuspensionLease(this);
    }

    public void Shutdown(bool restoreMainWindow = false)
    {
        lock (_stateLock)
        {
            _isShuttingDown = true;
        }

        Stop(restoreMainWindow);
    }

    public void Stop(bool restoreMainWindow = false)
    {
        _timer.Stop();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _continuousCaptureLease?.Dispose();
        _continuousCaptureLease = null;

        if (restoreMainWindow && _hiddenByThisService)
        {
            classIslandSettingsService.SetMainWindowVisible(true);
            _hiddenByThisService = false;
        }
    }

    public void ApplyConfig()
    {
        bool isSuspended;
        lock (_stateLock)
        {
            isSuspended = _isShuttingDown || _suspensionCount > 0;
        }

        Stop(restoreMainWindow: !isSuspended && !configHandler.Data.AutoHideMainWindowWhenOccluded);
        if (isSuspended)
        {
            return;
        }

        if (!configHandler.Data.AutoHideMainWindowWhenOccluded || !OperatingSystem.IsWindows())
        {
            return;
        }

        _ocrEngine ??= CreateOcrEngine();
        if (_ocrEngine == null)
        {
            logger.LogWarning("Windows 没有可用的本地 OCR 语言，无法检测主界面后方文字。");
            classIslandSettingsService.SetMainWindowVisible(true);
            return;
        }

        classIslandSettingsService.SetMainWindowVisible(true);
        _continuousCaptureLease = backgroundCaptureService.BeginContinuousCapture();
        _cancellationTokenSource = new CancellationTokenSource();
        _timer.Start();
        _ = DetectAndApplyAsync(_cancellationTokenSource.Token);
    }

    private void Resume()
    {
        bool shouldApply;
        lock (_stateLock)
        {
            if (_suspensionCount == 0)
            {
                return;
            }

            _suspensionCount--;
            shouldApply = _suspensionCount == 0 && !_isShuttingDown;
        }

        if (!shouldApply)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyConfig();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyConfig);
        }
    }

    private sealed class SuspensionLease(MainWindowTextOcclusionService service) : IDisposable
    {
        private MainWindowTextOcclusionService? _service = service;

        public void Dispose()
        {
            Interlocked.Exchange(ref _service, null)?.Resume();
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_cancellationTokenSource is { } source)
        {
            await DetectAndApplyAsync(source.Token);
        }
    }

    private async Task DetectAndApplyAsync(CancellationToken cancellationToken)
    {
        if (!await _recognitionLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            using var frame = await backgroundCaptureService.CaptureAsync(cancellationToken);
            if (frame == null ||
                cancellationToken.IsCancellationRequested ||
                IsSuspendedOrShuttingDown())
            {
                if (!cancellationToken.IsCancellationRequested &&
                    !IsSuspendedOrShuttingDown())
                {
                    classIslandSettingsService.SetMainWindowVisible(true);
                    _hiddenByThisService = false;
                }
                return;
            }

            var characterCount = await CountRecognizedCharactersAsync(frame, cancellationToken);
            if (cancellationToken.IsCancellationRequested || IsSuspendedOrShuttingDown())
            {
                return;
            }

            var shouldHide = characterCount >= MinimumTextCharacterCount;
            var changed = classIslandSettingsService.SetMainWindowVisible(!shouldHide);
            if (shouldHide && changed)
            {
                _hiddenByThisService = true;
            }
            else if (!shouldHide)
            {
                _hiddenByThisService = false;
            }
            logger.LogDebug("主界面后方识别到 {CharacterCount} 个文字字符，执行{Action}主界面。",
                characterCount, shouldHide ? "隐藏" : "显示");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "检测主界面后方文字失败，将在下次计时重试。");
            if (!cancellationToken.IsCancellationRequested && !IsSuspendedOrShuttingDown())
            {
                classIslandSettingsService.SetMainWindowVisible(true);
                _hiddenByThisService = false;
            }
        }
        finally
        {
            _recognitionLock.Release();
        }
    }

    private bool IsSuspendedOrShuttingDown()
    {
        lock (_stateLock)
        {
            return _isShuttingDown || _suspensionCount > 0;
        }
    }

    private async Task<int> CountRecognizedCharactersAsync(
        MainWindowBackgroundFrame frame,
        CancellationToken cancellationToken)
    {
        if (_ocrEngine == null)
        {
            return 0;
        }

        var count = 0;
        foreach (var region in frame.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var ocrBitmap = ResizeForOcr(region.Bitmap);
            using var softwareBitmap = ConvertToSoftwareBitmap(ocrBitmap);
            var result = await _ocrEngine.RecognizeAsync(softwareBitmap);
            count += result.Text.Count(IsTextCharacter);
            if (count >= MinimumTextCharacterCount)
            {
                break;
            }
        }

        return count;
    }

    private static SoftwareBitmap ConvertToSoftwareBitmap(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowLength = bitmap.Width * 4;
            var pixels = new byte[rowLength * bitmap.Height];
            for (var row = 0; row < bitmap.Height; row++)
            {
                var source = IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride);
                Marshal.Copy(source, pixels, row * rowLength, rowLength);
            }

            var softwareBitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8,
                bitmap.Width,
                bitmap.Height,
                BitmapAlphaMode.Ignore);
            softwareBitmap.CopyFromBuffer(pixels.AsBuffer());
            return softwareBitmap;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static Bitmap ResizeForOcr(Bitmap source)
    {
        var maximumDimension = OcrEngine.MaxImageDimension;
        if (source.Width <= maximumDimension && source.Height <= maximumDimension)
        {
            return source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        }

        var scale = Math.Min((double)maximumDimension / source.Width,
            (double)maximumDimension / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    private static OcrEngine? CreateOcrEngine()
    {
        var preferred = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(language =>
            language.LanguageTag.StartsWith(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                StringComparison.OrdinalIgnoreCase));

        return preferred == null
            ? OcrEngine.TryCreateFromUserProfileLanguages()
            : OcrEngine.TryCreateFromLanguage(preferred);
    }

    private static bool IsTextCharacter(char character)
    {
        return char.IsLetterOrDigit(character) ||
               character is >= '\u3400' and <= '\u9FFF' ||
               character is >= '\u3040' and <= '\u30FF' ||
               character is >= '\uAC00' and <= '\uD7AF';
    }
}

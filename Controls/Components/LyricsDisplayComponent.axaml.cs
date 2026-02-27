using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SystemTools.Models.ComponentSettings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Bitmap = System.Drawing.Bitmap;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;

namespace SystemTools.Controls.Components;

[ComponentInfo(
    "A1B2C3D4-E5F6-7890-ABCD-EF1234567891",
    "音乐软件歌词显示",
    "\uEBDC",
    "实时显示选定的音乐软件歌词"
)]
public partial class LyricsDisplayComponent : ComponentBase<LyricsDisplaySettings>, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _bottomTimer;
    private Avalonia.Media.Imaging.Bitmap? _lyricsBitmap;
    private IntPtr _targetWindowHandle;
    private IntPtr _lyricsWindowHandle;
    private bool _isBottomTimerRunning = false;

    private int _originalWidth;
    private int _originalHeight;

    // 计算属性
    public double ScaledWidth => _originalWidth * Settings.ImageScale;
    public double ScaledHeight => _originalHeight * Settings.ImageScale;

    public Avalonia.Media.Imaging.Bitmap? LyricsBitmap
    {
        get => _lyricsBitmap;
        set
        {
            _lyricsBitmap = value;
            OnPropertyChanged(nameof(LyricsBitmap));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Win32 API
    //[DllImport("user32.dll", SetLastError = true)]
    //static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    //[DllImport("user32.dll")]
    //static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

    //[DllImport("user32.dll")]
    //static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    //[DllImport("gdi32.dll")]
    //static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    //[DllImport("gdi32.dll")]
    //static extern bool DeleteDC(IntPtr hdc);

    //[DllImport("gdi32.dll")]
    //static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    //[DllImport("gdi32.dll")]
    //static extern bool DeleteObject(IntPtr hObject);

    //[DllImport("user32.dll")]
    //static extern IntPtr GetDC(IntPtr hWnd);

    //[DllImport("user32.dll")]
    //static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    //[DllImport("user32.dll")]
    //static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    //[DllImport("user32.dll")]
    //static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    //[DllImport("user32.dll", SetLastError = true)]
    //static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    //[DllImport("user32.dll")]
    //static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    //const uint SWP_NOSIZE = 0x0001;
    //const uint SWP_NOMOVE = 0x0002;
    //const uint SWP_NOACTIVATE = 0x0010;

    //[StructLayout(LayoutKind.Sequential)]
    //public struct RECT
    //{
    //    public int Left;
    //    public int Top;
    //    public int Right;
    //    public int Bottom;
    //}

    public LyricsDisplayComponent()
    {
        InitializeComponent();
        _timer = new DispatcherTimer();
        _timer.Tick += OnTimer_Ticked;

        _bottomTimer = new DispatcherTimer();
        _bottomTimer.Interval = TimeSpan.FromMilliseconds(80);
        _bottomTimer.Tick += OnBottomTimer_Ticked;
    }

    private void LyricsDisplayComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Settings.RefreshRate);
        _timer.Start();

        UpdateBottomTimerState();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void LyricsDisplayComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _bottomTimer.Stop();
        _isBottomTimerRunning = false;

        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.KeepLyricsWindowBottom))
        {
            UpdateBottomTimerState();
        }
    }

    private void UpdateBottomTimerState()
    {
        if (Settings.KeepLyricsWindowBottom && !_isBottomTimerRunning)
        {
            _bottomTimer.Start();
            _isBottomTimerRunning = true;
        }
        else if (!Settings.KeepLyricsWindowBottom && _isBottomTimerRunning)
        {
            _bottomTimer.Stop();
            _isBottomTimerRunning = false;
        }
    }

    private void OnTimer_Ticked(object? sender, EventArgs e)
    {
        CaptureLyricsWindow();
    }

    private void OnBottomTimer_Ticked(object? sender, EventArgs e)
    {
        try
        {
            if (Settings.SelectedMusicSoftware == MusicSoftware.LyricifyLite)
            {
                _lyricsWindowHandle = FindWindowByClassPrefix(Settings.TargetWindowClassName, Settings.TargetWindowTitle);
            }
            else
            {
                _lyricsWindowHandle = PInvoke.FindWindow(Settings.TargetWindowClassName, Settings.TargetWindowTitle);
            }

            if (_lyricsWindowHandle != IntPtr.Zero)
            {
                PInvoke.SetWindowPos(new HWND(_lyricsWindowHandle), (HWND)HWND_BOTTOM, 0, 0, 0, 0,
                    Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOSIZE | Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOMOVE | Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            }
        }
        catch
        {
        }
    }

    private void CaptureLyricsWindow()
    {
        try
        {
            if (Settings.KeepLyricsWindowBottom && _lyricsWindowHandle != IntPtr.Zero)
            {
                _targetWindowHandle = _lyricsWindowHandle;
            }
            else
            {
                // Lyricify Lite 使用前缀匹配
                if (Settings.SelectedMusicSoftware == MusicSoftware.LyricifyLite)
                {
                    _targetWindowHandle = FindWindowByClassPrefix(Settings.TargetWindowClassName, Settings.TargetWindowTitle);
                }
                else
                {
                    _targetWindowHandle = PInvoke.FindWindow(Settings.TargetWindowClassName, Settings.TargetWindowTitle);
                }
            }

            if (_targetWindowHandle == IntPtr.Zero)
            {
                LyricsBitmap = null;
                return;
            }

            PInvoke.GetWindowRect(new HWND(_targetWindowHandle), out RECT rect);
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;

            if (width <= 0 || height <= 0)
            {
                LyricsBitmap = null;
                return;
            }

            using (Bitmap originalBmp = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(originalBmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PInvoke.PrintWindow(new HWND(_targetWindowHandle), new HDC(hdc), (Windows.Win32.Storage.Xps.PRINT_WINDOW_FLAGS)0x2);
                    g.ReleaseHdc(hdc);
                }

                Rectangle cropArea;
                if (Settings.SelectedMusicSoftware == MusicSoftware.QishuiMusic)
                {
                    // 汽水音乐
                    int cropTop = (int)(height * 0.15);
                    int cropBottom = (int)(height * 0.57);
                    int croppedHeight = height - cropTop - cropBottom;
                    cropArea = new Rectangle(0, cropTop, width, croppedHeight);
                }
                else
                {
                    // 其他
                    int cropTop = height / 4;
                    int croppedHeight = height - cropTop;
                    cropArea = new Rectangle(0, cropTop, width, croppedHeight);
                }

                using (Bitmap croppedBmp = originalBmp.Clone(cropArea, PixelFormat.Format32bppArgb))
                {
                    ProcessBlackPixels(croppedBmp);
                    LyricsBitmap = ConvertToAvaloniaBitmap(croppedBmp);
                    _originalWidth = croppedBmp.Width;
                    _originalHeight = croppedBmp.Height;
                }
            }

            OnPropertyChanged(nameof(ScaledWidth));
            OnPropertyChanged(nameof(ScaledHeight));
        }
        catch
        {
            LyricsBitmap = null;
        }
    }

    private IntPtr FindWindowByClassPrefix(string classPrefix, string? windowTitle)
    {
        IntPtr foundHandle = IntPtr.Zero;

        PInvoke.EnumWindows((hWnd, lParam) =>
        {
            Span<char> buffer = stackalloc char[256];
            int length=PInvoke.GetClassName(hWnd, buffer);
            if (length == 0) return false;
            string className = new(buffer.Slice(0, length));
            
            if (className.StartsWith(classPrefix))
            {
                if (windowTitle != null)
                {
                    Span<char> buffer2 = stackalloc char[256];
                    int length2=PInvoke.GetWindowText(hWnd, buffer2);
                    if (length2 == 0) return false;
                    string title = new(buffer2.Slice(0, length2));
                    
                    if (title == windowTitle)
                    {
                        foundHandle = hWnd;
                        return false;
                    }
                }
                else
                {
                    foundHandle = hWnd;
                    return false;
                }
            }
            
            return true;
        }, IntPtr.Zero);
        
        return foundHandle;
    }

    private void ProcessBlackPixels(Bitmap bitmap)
    {
        BitmapData? bmpData = null;
        try
        {
            bmpData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);

            int bytesPerPixel = 4;
            byte[] pixelBuffer = new byte[bmpData.Stride * bmpData.Height];
            Marshal.Copy(bmpData.Scan0, pixelBuffer, 0, pixelBuffer.Length);

            for (int y = 0; y < bitmap.Height; y++)
            {
                int currentLine = y * bmpData.Stride;
                for (int x = 0; x < bitmap.Width * bytesPerPixel; x += bytesPerPixel)
                {
                    int blue = pixelBuffer[currentLine + x];
                    int green = pixelBuffer[currentLine + x + 1];
                    int red = pixelBuffer[currentLine + x + 2];

                    if (red == 0 && green == 0 && blue == 0)
                    {
                        pixelBuffer[currentLine + x + 3] = 0;
                    }
                }
            }

            Marshal.Copy(pixelBuffer, 0, bmpData.Scan0, pixelBuffer.Length);
        }
        finally
        {
            if (bmpData != null)
            {
                bitmap.UnlockBits(bmpData);
            }
        }
    }

    private Avalonia.Media.Imaging.Bitmap? ConvertToAvaloniaBitmap(Bitmap bitmap)
    {
        try
        {
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return new Avalonia.Media.Imaging.Bitmap(memoryStream);
            }
        }
        catch
        {
            return null;
        }
    }
}
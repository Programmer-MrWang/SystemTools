using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;

namespace SystemTools.Controls;

public class HotkeyRecorderControl : Border
{
    private bool _isRecording = false;
    private int _capturedModifierKeys = 0;
    private uint _capturedVirtualKey = 0;
    private readonly TextBlock _displayText;

    public event EventHandler<HotkeyRecordedEventArgs>? HotkeyRecorded;

    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingEnded;

    public static readonly StyledProperty<string> HotkeyDisplayProperty =
        AvaloniaProperty.Register<HotkeyRecorderControl, string>(nameof(HotkeyDisplay), "点击录制热键");

    public static readonly StyledProperty<bool> IsRecordingProperty =
        AvaloniaProperty.Register<HotkeyRecorderControl, bool>(nameof(IsRecording));

    public string HotkeyDisplay
    {
        get => GetValue(HotkeyDisplayProperty);
        set => SetValue(HotkeyDisplayProperty, value);
    }

    public bool IsRecording
    {
        get => GetValue(IsRecordingProperty);
        private set => SetValue(IsRecordingProperty, value);
    }

    public int ModifierKeys => _capturedModifierKeys;
    public uint VirtualKey => _capturedVirtualKey;

    public HotkeyRecorderControl()
    {
        // 设置容器样式
        Background = new SolidColorBrush(Color.Parse("#E8E8E8"));
        BorderBrush = new SolidColorBrush(Color.Parse("#D0D0D0"));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12, 8);
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);

        // 创建文字显示
        // 创建文字显示
        _displayText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#666666"))
        };

        // 绑定文字
        _displayText.Bind(TextBlock.TextProperty, this.GetObservable(HotkeyDisplayProperty).ToBinding());

        // 设置内容
        Child = _displayText;

        // 悬停效果
        this.PointerEntered += (s, e) =>
        {
            if (!_isRecording)
            {
                Background = new SolidColorBrush(Color.Parse("#DEDEDE"));
            }
        };

        this.PointerExited += (s, e) =>
        {
            if (!_isRecording)
            {
                Background = new SolidColorBrush(Color.Parse("#E8E8E8"));
            }
        };

        this.PointerPressed += OnPointerPressed;
        this.KeyDown += OnKeyDown;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isRecording)
        {
            StartRecording();
        }
        else
        {
            CancelRecording();
        }
    }

    public void StartRecording()
    {
        _isRecording = true;
        IsRecording = true;
        HotkeyDisplay = "按下热键组合...";
        Background = new SolidColorBrush(Color.Parse("#FFF3E0"));
        BorderBrush = new SolidColorBrush(Color.Parse("#FF9800"));

        RecordingStarted?.Invoke(this, EventArgs.Empty);

        this.Focus();
    }

    public void CancelRecording()
    {
        _isRecording = false;
        IsRecording = false;
        HotkeyDisplay = "点击此处开始录制";
        Background = new SolidColorBrush(Color.Parse("#E8E8E8"));
        BorderBrush = new SolidColorBrush(Color.Parse("#D0D0D0"));

        RecordingEnded?.Invoke(this, EventArgs.Empty);
    }

    public void SetHotkey(int modifierKeys, uint virtualKey, string display)
    {
        _capturedModifierKeys = modifierKeys;
        _capturedVirtualKey = virtualKey;
        HotkeyDisplay = display;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isRecording) return;

        e.Handled = true;

        // 忽略单独的修饰键
        if (IsModifierKey(e.Key))
        {
            return;
        }

        // 捕获修饰键状态
        _capturedModifierKeys = 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) _capturedModifierKeys |= 0x0002;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) _capturedModifierKeys |= 0x0001;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _capturedModifierKeys |= 0x0004;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) _capturedModifierKeys |= 0x0008;

        // 转换 Avalonia Key 到 Win32 VK
        _capturedVirtualKey = AvaloniaKeyToVirtualKey(e.Key);

        _isRecording = false;
        IsRecording = false;

        Background = new SolidColorBrush(Color.Parse("#E8F5E9"));
        BorderBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
        RecordingEnded?.Invoke(this, EventArgs.Empty);

        HotkeyRecorded?.Invoke(this, new HotkeyRecordedEventArgs
        {
            ModifierKeys = _capturedModifierKeys,
            VirtualKey = _capturedVirtualKey
        });
    }

    private bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or
               Key.LeftAlt or Key.RightAlt or
               Key.LeftShift or Key.RightShift or
               Key.LWin or Key.RWin;
    }

    private uint AvaloniaKeyToVirtualKey(Key key)
    {
        return key switch
        {
            Key.F1 => 0x70,
            Key.F2 => 0x71,
            Key.F3 => 0x72,
            Key.F4 => 0x73,
            Key.F5 => 0x74,
            Key.F6 => 0x75,
            Key.F7 => 0x76,
            Key.F8 => 0x77,
            Key.F9 => 0x78,
            Key.F10 => 0x79,
            Key.F11 => 0x7A,
            Key.F12 => 0x7B,
            Key.A => 0x41,
            Key.B => 0x42,
            Key.C => 0x43,
            Key.D => 0x44,
            Key.E => 0x45,
            Key.F => 0x46,
            Key.G => 0x47,
            Key.H => 0x48,
            Key.I => 0x49,
            Key.J => 0x4A,
            Key.K => 0x4B,
            Key.L => 0x4C,
            Key.M => 0x4D,
            Key.N => 0x4E,
            Key.O => 0x4F,
            Key.P => 0x50,
            Key.Q => 0x51,
            Key.R => 0x52,
            Key.S => 0x53,
            Key.T => 0x54,
            Key.U => 0x55,
            Key.V => 0x56,
            Key.W => 0x57,
            Key.X => 0x58,
            Key.Y => 0x59,
            Key.Z => 0x5A,
            Key.D0 => 0x30,
            Key.D1 => 0x31,
            Key.D2 => 0x32,
            Key.D3 => 0x33,
            Key.D4 => 0x34,
            Key.D5 => 0x35,
            Key.D6 => 0x36,
            Key.D7 => 0x37,
            Key.D8 => 0x38,
            Key.D9 => 0x39,
            Key.NumPad0 => 0x60,
            Key.NumPad1 => 0x61,
            Key.NumPad2 => 0x62,
            Key.NumPad3 => 0x63,
            Key.NumPad4 => 0x64,
            Key.NumPad5 => 0x65,
            Key.NumPad6 => 0x66,
            Key.NumPad7 => 0x67,
            Key.NumPad8 => 0x68,
            Key.NumPad9 => 0x69,
            Key.Space => 0x20,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Up => 0x26,
            Key.Down => 0x28,
            Key.Left => 0x25,
            Key.Right => 0x27,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            _ => 0
        };
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_isRecording)
        {
            CancelRecording();
        }
        base.OnDetachedFromVisualTree(e);
    }
}

public class HotkeyRecordedEventArgs : EventArgs
{
    public int ModifierKeys { get; set; }
    public uint VirtualKey { get; set; }
}
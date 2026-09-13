using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SystemTools.Helpers;

/// <summary>
/// 读取与应用 Windows 系统强调色（Accent Color）。
/// </summary>
/// <remarks>
/// <para>
/// 只切换“强调色”本身，<b>不改变强调色的应用范围</b>：不会打开“在开始菜单和任务栏上显示强调色”
/// 或“在标题栏和窗口边框上显示强调色”，因此任务栏颜色、系统应用窗口边框颜色保持用户原有设置不变。
/// </para>
/// <para>
/// Windows 没有公开的“设置强调色”接口，这里叠加两条路径以做到“无需重启任何进程即可立即生效”：
/// <list type="number">
/// <item>写入注册表（使用各自正确的 ABGR / ARGB 字节序并生成强调色调色板），保证持久化；</item>
/// <item>调用“设置”应用自身使用的未公开导出 <c>uxtheme!SetUserColorPreference</c>（序号 122），
/// 使强调色即时生效，无需重启资源管理器。</item>
/// </list>
/// 未公开接口不可用时静默降级为“仅注册表 + 广播”，不影响功能可用性。
/// </para>
/// </remarks>
public static class WindowsAccentColor
{
    private const string DwmKeyPath = @"Software\Microsoft\Windows\DWM";
    private const string AccentKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DesktopKeyPath = @"Control Panel\Desktop";

    private const uint HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    // COLORREF 仅使用低 24 位，格式为 0x00BBGGRR。
    private const uint ColorRefMask = 0x00FFFFFF;

    /// <summary>
    /// 参与快照/还原的注册表值。
    /// 有意不包含 <c>ColorPrevalence</c>：本功能不改变强调色的应用范围。
    /// </summary>
    private static readonly (string Path, string Name)[] TrackedValues =
    [
        (DwmKeyPath, "AccentColor"),
        (DwmKeyPath, "ColorizationColor"),
        (DwmKeyPath, "ColorizationAfterglow"),
        (AccentKeyPath, "AccentColorMenu"),
        (AccentKeyPath, "StartColorMenu"),
        (AccentKeyPath, "AccentPalette"),
        (DesktopKeyPath, "AutoColorization"),
    ];

    /// <summary>一个 RGB 颜色。</summary>
    public readonly record struct AccentRgb(byte R, byte G, byte B);

    /// <summary>
    /// 解析颜色字符串，支持 <c>#RRGGBB</c>、<c>RRGGBB</c> 以及带 Alpha 的 <c>#AARRGGBB</c>。
    /// </summary>
    public static bool TryParseHex(string? hex, out AccentRgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim().TrimStart('#');
        // Avalonia 的颜色字符串形如 #AARRGGBB，去掉 Alpha 后取后六位。
        if (text.Length == 8)
        {
            text = text[2..];
        }

        if (text.Length != 6 ||
            !byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new AccentRgb(r, g, b);
        return true;
    }

    /// <summary>
    /// 仅切换系统强调色：写入强调色相关注册表值、通过未公开接口即时提交，并广播强调色变更。
    /// 不修改 <c>ColorPrevalence</c>（任务栏/标题栏是否使用强调色），也不改变窗口边框配色。
    /// </summary>
    public static void Apply(AccentRgb color)
    {
        // 记录应用前的窗口边框配色，稍后原样恢复。
        var hadColorization = TryGetDwmColorizationParameters(out var originalColorization);

        // DWM\AccentColor 与 Explorer\Accent 的两个菜单色为 ABGR（0xAABBGGRR，Alpha 常为 FF）。
        var abgr = 0xFF000000u | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;

        WriteValue(DwmKeyPath, "AccentColor", unchecked((int)abgr), RegistryValueKind.DWord);
        WriteValue(AccentKeyPath, "AccentColorMenu", unchecked((int)abgr), RegistryValueKind.DWord);
        WriteValue(AccentKeyPath, "StartColorMenu", unchecked((int)abgr), RegistryValueKind.DWord);
        WriteValue(AccentKeyPath, "AccentPalette", BuildAccentPalette(color), RegistryValueKind.Binary);

        // 关闭“从背景自动选取强调色”，确保手动选择的颜色不被壁纸覆盖。
        WriteValue(DesktopKeyPath, "AutoColorization", 0, RegistryValueKind.DWord);

        // 通过未公开接口即时提交（不可用时仅依赖注册表与广播）。
        TryApplyImmediate(color);

        // 该接口会顺带把窗口边框配色改成强调色；恢复为用户原值，做到“只换强调色”。
        if (hadColorization)
        {
            TrySetDwmColorizationParameters(originalColorization);
        }

        // 通知应用与外壳强调色已变更。
        BroadcastSettingsChange("ImmersiveColorSet");
    }

    /// <summary>捕获当前强调色相关状态，供之后还原。</summary>
    public static AccentColorSnapshot Capture()
    {
        var snapshot = new AccentColorSnapshot();

        if (TryGetImmersiveColorPreference(out var preference))
        {
            snapshot.ImmersivePreference = preference;
        }

        foreach (var (path, name) in TrackedValues)
        {
            snapshot.RegistryValues[(path, name)] = ReadValue(path, name);
        }

        return snapshot;
    }

    /// <summary>把系统强调色还原为快照时的状态。</summary>
    public static void Restore(AccentColorSnapshot snapshot)
    {
        foreach (var ((path, name), entry) in snapshot.RegistryValues)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
            if (key is null)
            {
                continue;
            }

            if (entry is { } value)
            {
                key.SetValue(name, value.Data, value.Kind);
            }
            else
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
        }

        // 通过未公开接口把内存中的偏好一并还原，做到即时生效。
        if (snapshot.ImmersivePreference is { } preference)
        {
            TrySetImmersiveColorPreference(preference);
        }

        BroadcastSettingsChange("ImmersiveColorSet");
    }

    private static void WriteValue(string path, string name, object value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException($"无法打开注册表项：HKCU\\{path}");
        }

        key.SetValue(name, value, kind);
    }

    private static RegistryEntry? ReadValue(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key is null)
        {
            return null;
        }

        try
        {
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null ? null : new RegistryEntry(key.GetValueKind(name), value);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 生成 <c>AccentPalette</c>（32 字节 = 8 × RGBA）。
    /// Windows 的顺序为：索引 0–2 更亮、索引 3 为基准色、索引 4–6 更暗，Alpha 字节为 0x00。
    /// </summary>
    private static byte[] BuildAccentPalette(AccentRgb color)
    {
        var (hue, saturation, lightness) = RgbToHsl(color);

        double[] levels =
        [
            lightness + (1 - lightness) * 0.66,
            lightness + (1 - lightness) * 0.40,
            lightness + (1 - lightness) * 0.12,
            lightness,
            lightness - lightness * 0.10,
            lightness - lightness * 0.31,
            lightness - lightness * 0.51,
            lightness,
        ];

        var palette = new byte[32];
        for (var i = 0; i < levels.Length; i++)
        {
            var rgb = HslToRgb(hue, saturation, Math.Clamp(levels[i], 0, 1));
            var offset = i * 4;
            palette[offset] = rgb.R;
            palette[offset + 1] = rgb.G;
            palette[offset + 2] = rgb.B;
            palette[offset + 3] = 0x00;
        }

        return palette;
    }

    private static (double Hue, double Saturation, double Lightness) RgbToHsl(AccentRgb color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2.0;
        var delta = max - min;

        if (delta < 1e-9)
        {
            return (0, 0, lightness);
        }

        var saturation = lightness > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        double hue;
        if (max <= r + 1e-9 && max >= r - 1e-9)
        {
            hue = 60 * ((g - b) / delta);
        }
        else if (max <= g + 1e-9 && max >= g - 1e-9)
        {
            hue = 60 * ((b - r) / delta + 2);
        }
        else
        {
            hue = 60 * ((r - g) / delta + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, saturation, lightness);
    }

    private static AccentRgb HslToRgb(double hue, double saturation, double lightness)
    {
        if (saturation < 1e-9)
        {
            var gray = ToByte(lightness);
            return new AccentRgb(gray, gray, gray);
        }

        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var hp = hue / 60.0;
        var x = c * (1 - Math.Abs(hp % 2 - 1));
        var m = lightness - c / 2;

        double r1, g1, b1;
        switch ((int)Math.Floor(hp) % 6)
        {
            case 0: (r1, g1, b1) = (c, x, 0); break;
            case 1: (r1, g1, b1) = (x, c, 0); break;
            case 2: (r1, g1, b1) = (0, c, x); break;
            case 3: (r1, g1, b1) = (0, x, c); break;
            case 4: (r1, g1, b1) = (x, 0, c); break;
            default: (r1, g1, b1) = (c, 0, x); break;
        }

        return new AccentRgb(ToByte(r1 + m), ToByte(g1 + m), ToByte(b1 + m));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static uint ToColorRef(AccentRgb color) =>
        color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

    /// <summary>
    /// 通过未公开接口把强调色即时写入内存偏好，无需重启资源管理器。
    /// </summary>
    private static bool TryApplyImmediate(AccentRgb color)
    {
        if (!TryGetImmersiveColorPreference(out var preference))
        {
            return false;
        }

        preference.color2 = ToColorRef(color) & ColorRefMask;
        return TrySetImmersiveColorPreference(preference);
    }

    private static bool TryGetImmersiveColorPreference(out ImmersiveColorPreference preference)
    {
        preference = default;
        try
        {
            return GetUserColorPreference(ref preference, 0) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetImmersiveColorPreference(ImmersiveColorPreference preference)
    {
        try
        {
            return SetUserColorPreference(ref preference, 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDwmColorizationParameters(out DwmColorizationParameters parameters)
    {
        parameters = default;
        try
        {
            return DwmGetColorizationParameters(out parameters) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetDwmColorizationParameters(DwmColorizationParameters parameters)
    {
        try
        {
            return DwmSetColorizationParameters(ref parameters, 0) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void BroadcastSettingsChange(string area)
    {
        try
        {
            SendMessageTimeoutString((IntPtr)HwndBroadcast, WmSettingChange, IntPtr.Zero, area,
                SmtoAbortIfHung, 5000, out _);
        }
        catch
        {
            // 广播失败不影响已写入的注册表状态。
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutString(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("uxtheme.dll", EntryPoint = "GetUserColorPreference", ExactSpelling = true)]
    private static extern int GetUserColorPreference(ref ImmersiveColorPreference preference, int forceReload);

    [DllImport("uxtheme.dll", EntryPoint = "#122", ExactSpelling = true)]
    private static extern int SetUserColorPreference(ref ImmersiveColorPreference preference, int forceCommit);

    [DllImport("dwmapi.dll", EntryPoint = "#127", ExactSpelling = true)]
    private static extern int DwmGetColorizationParameters(out DwmColorizationParameters parameters);

    [DllImport("dwmapi.dll", EntryPoint = "#131", ExactSpelling = true)]
    private static extern int DwmSetColorizationParameters(ref DwmColorizationParameters parameters, uint unknown);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ImmersiveColorPreference
    {
        public uint color1;
        public uint color2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DwmColorizationParameters
    {
        public uint dwColor;
        public uint dwAfterglow;
        public uint dwColorBalance;
        public uint dwAfterglowBalance;
        public uint dwBlurBalance;
        public uint dwGlassReflectionIntensity;
        public uint dwOpaqueBlend;
    }

    internal readonly record struct RegistryEntry(RegistryValueKind Kind, object Data);
}

/// <summary>强调色状态快照，用于把系统强调色还原到应用前的状态。</summary>
public sealed class AccentColorSnapshot
{
    internal WindowsAccentColor.ImmersiveColorPreference? ImmersivePreference { get; set; }

    internal Dictionary<(string Path, string Name), WindowsAccentColor.RegistryEntry?> RegistryValues { get; } = new();
}

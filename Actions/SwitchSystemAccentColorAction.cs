using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SwitchSystemAccentColor", "切换系统强调色", "\uE523", false)]
public class SwitchSystemAccentColorAction(ILogger<SwitchSystemAccentColorAction> logger) : ActionBase<AccentColorSettings>
{
    private readonly ILogger<SwitchSystemAccentColorAction> _logger = logger;

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    const uint HWND_BROADCAST = 0xFFFF;
    const uint WM_SETTINGCHANGE = 0x001A;
    const uint SMTO_ABORTIFHUNG = 0x0002;

    protected override async Task OnInvoke()
    {
        if (Settings == null || string.IsNullOrWhiteSpace(Settings.ColorHex)) return;

        try
        {
            var color = ParseColor(Settings.ColorHex);
            var dword = ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
            var colorizationDword = (0xC4u << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;

            using var dwmKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM");
            dwmKey?.SetValue("AccentColor", unchecked((int)dword), RegistryValueKind.DWord);
            dwmKey?.SetValue("ColorizationColor", unchecked((int)colorizationDword), RegistryValueKind.DWord);
            dwmKey?.SetValue("ColorizationAfterglow", unchecked((int)colorizationDword), RegistryValueKind.DWord);
            dwmKey?.SetValue("ColorPrevalence", 1, RegistryValueKind.DWord);

            using var explorerKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            explorerKey?.SetValue("AccentColorMenu", unchecked((int)dword), RegistryValueKind.DWord);
            explorerKey?.SetValue("StartColorMenu", unchecked((int)dword), RegistryValueKind.DWord);
            explorerKey?.SetValue("AccentPalette", BuildAccentPalette(color.R, color.G, color.B), RegistryValueKind.Binary);

            SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, (UIntPtr)0, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 5000, out _);

            _logger.LogInformation("系统强调色已切换为 {Color}", Settings.ColorHex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换系统强调色失败");
            throw;
        }

        await base.OnInvoke();
    }

    private static (byte A, byte R, byte G, byte B) ParseColor(string colorHex)
    {
        var hex = colorHex.Trim().TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        if (hex.Length != 8) throw new FormatException("颜色格式无效");

        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private static byte[] BuildAccentPalette(byte r, byte g, byte b)
    {
        var colors = new List<(byte R, byte G, byte B)>
        {
            Scale(r, g, b, 0.60),
            Scale(r, g, b, 0.75),
            Scale(r, g, b, 0.90),
            (r, g, b),
            Scale(r, g, b, 1.10),
            Scale(r, g, b, 1.25),
            Scale(r, g, b, 1.40),
            Scale(r, g, b, 1.55)
        };

        var palette = new byte[32];
        for (var i = 0; i < colors.Count; i++)
        {
            var c = colors[i];
            var p = i * 4;
            palette[p] = c.R;
            palette[p + 1] = c.G;
            palette[p + 2] = c.B;
            palette[p + 3] = 0xFF;
        }

        return palette;
    }

    private static (byte R, byte G, byte B) Scale(byte r, byte g, byte b, double factor)
    {
        static byte ClampToByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
        return (ClampToByte(r * factor), ClampToByte(g * factor), ClampToByte(b * factor));
    }
}

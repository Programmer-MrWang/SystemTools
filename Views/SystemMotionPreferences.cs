using System;
using System.Runtime.InteropServices;

namespace SystemTools.Views;

internal static class SystemMotionPreferences
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public static bool ShouldReduceMotion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return SystemParametersInfo(
                       SpiGetClientAreaAnimation,
                       0,
                       out var enabled,
                       0) &&
                   enabled == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        out int pvParam,
        uint fWinIni);
}

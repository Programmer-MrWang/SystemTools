namespace SystemTools.Common.Layout;

public static class HardwareBrightnessPolicy
{
    public static void Validate(double percent)
    {
        if (!double.IsFinite(percent) || percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent), "亮度必须在 0 到 100 之间。");
    }
    public static int ToRaw(double percent, int maximum)
    {
        Validate(percent);
        if (maximum <= 0) throw new IOException("显示器返回的亮度范围无效。");
        return (int)Math.Round(percent / 100 * maximum);
    }
    public static double ToPercent(int current, int maximum)
    {
        if (maximum <= 0 || current < 0 || current > maximum) throw new IOException("显示器返回的亮度读数无效。");
        return 100.0 * current / maximum;
    }
}

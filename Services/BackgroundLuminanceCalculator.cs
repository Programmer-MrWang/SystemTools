namespace SystemTools.Services;

internal static class BackgroundLuminanceCalculator
{
    public const double DarkThreshold = 128;

    public static double? CalculateAverage(MainWindowBackgroundFrame frame)
    {
        double totalLuminance = 0;
        long sampleCount = 0;

        foreach (var region in frame.Regions)
        {
            var bitmap = region.Bitmap;

            const int sampleStep = 8;
            for (var y = 0; y < bitmap.Height; y += sampleStep)
            {
                for (var x = 0; x < bitmap.Width; x += sampleStep)
                {
                    var color = bitmap.GetPixel(x, y);
                    totalLuminance += 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                    sampleCount++;
                }
            }
        }

        return sampleCount == 0 ? null : totalLuminance / sampleCount;
    }
}

using SystemTools.Contracts;

namespace SystemTools.Common.Layout;

public sealed class TextOcclusionPolicy
{
    private int _hits, _clear;
    public bool Hidden { get; private set; }
    public bool Observe(ScreenRegion region, IReadOnlyList<RecognizedText> text)
    {
        var occupied = text.Any(item => double.IsFinite(item.Confidence) && item.Confidence >= .6 &&
            item.Bounds.Width >= 4 && item.Bounds.Height >= 4 &&
            Math.Min(region.X + region.Width, item.Bounds.X + item.Bounds.Width) - Math.Max(region.X, item.Bounds.X) >= 4 &&
            Math.Min(region.Y + region.Height, item.Bounds.Y + item.Bounds.Height) - Math.Max(region.Y, item.Bounds.Y) >= 4);
        if (occupied) { _clear = 0; if (++_hits >= 2) Hidden = true; }
        else { _hits = 0; if (++_clear >= 3) Hidden = false; }
        return Hidden;
    }
    public void Reset() { _hits = _clear = 0; Hidden = false; }
}

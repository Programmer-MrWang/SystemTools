namespace SystemTools.Common.Layout;

/// <summary>A dead band and consecutive samples stop noisy backgrounds from continuously flipping the theme.</summary>
public sealed class BackgroundThemePolicy
{
    private int _candidate;
    private int _samples;
    public int? Observe(double luminance, int? current)
    {
        if (!double.IsFinite(luminance) || luminance < 0 || luminance > 255) { Reset(); return null; }
        var target = luminance < 112 ? 2 : luminance > 144 ? 1 : current;
        if (target is null || target == current) { Reset(); return null; }
        if (_candidate != target) { _candidate = target.Value; _samples = 0; }
        return ++_samples >= 2 ? target : null;
    }
    public void Reset() { _candidate = 0; _samples = 0; }
}

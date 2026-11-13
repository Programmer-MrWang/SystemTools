using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace SystemTools.Compatibility;

internal static class CompatibilityStyles
{
    public static IStyle Load(string path)
    {
        if (path.StartsWith("Themes/", StringComparison.Ordinal))
            return (IStyle)AvaloniaXamlLoader.Load(new Uri("avares://SystemTools.SharedHost/" + path));
        if (!OperatingSystem.IsWindows()) return new Styles();
        return (IStyle)AvaloniaXamlLoader.Load(
            new Uri("avares://SystemTools.Platforms.Windows/" + path));
    }
}

public sealed class CardTypeStyles : Styles
{
    public CardTypeStyles() => Add(CompatibilityStyles.Load("Themes/CardTypeComponent/Styles.axaml"));
}

public sealed class ClassWidgetsStyles : Styles
{
    public ClassWidgetsStyles() => Add(CompatibilityStyles.Load("Themes/ClassWidgets/Styles.axaml"));
}

public sealed class NotchStyles : Styles
{
    public NotchStyles() => Add(CompatibilityStyles.Load("Themes/NotchStyle/Styles.axaml"));
}

public sealed class SettingsPageStyles : Styles
{
    public SettingsPageStyles() => Add(CompatibilityStyles.Load("Controls/SettingsPageLazy.axaml"));
}

using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System.Reflection;

namespace SystemTools.Compatibility;

internal static class WindowsStyles
{
    public static IStyle Load(string path)
    {
        if (!OperatingSystem.IsWindows())
            return new Styles();
        return (IStyle)AvaloniaXamlLoader.Load(
            new Uri("avares://SystemTools.Platforms.Windows/" + path));
    }
}

public sealed class CardTypeStyles : Styles
{
    public CardTypeStyles() => Add(WindowsStyles.Load("Themes/CardTypeComponent/Styles.axaml"));
}

public sealed class ClassWidgetsStyles : Styles
{
    public ClassWidgetsStyles() => Add(WindowsStyles.Load("Themes/ClassWidgets/Styles.axaml"));
}

public sealed class NotchStyles : Styles
{
    public NotchStyles() => Add(WindowsStyles.Load("Themes/NotchStyle/Styles.axaml"));
}

public sealed class SettingsPageStyles : Styles
{
    public SettingsPageStyles() => Add(WindowsStyles.Load("Controls/SettingsPageLazy.axaml"));
}


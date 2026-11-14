using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SystemTools.Themes.NotchStyle;

public sealed class NotchStyleStyles : Styles
{
    private static readonly Uri ThemeResourceUri =
        new("avares://SystemTools/Themes/NotchStyle/Theme.axaml.txt");

    public NotchStyleStyles()
    {
        // Runtime XAML resolves qualified control names through Assembly.Load.
        // Keep those lookups inside the plugin's isolated load context.
        using var reflection = AssemblyLoadContext.EnterContextualReflection(typeof(NotchStyleStyles).Assembly);
        using var stream = AssetLoader.Open(ThemeResourceUri);
        using var reader = new StreamReader(stream);
        if (AvaloniaRuntimeXamlLoader.Load(
                reader.ReadToEnd().Replace("assembly=SystemTools\"",
                    "assembly=" + typeof(NotchStyleStyles).Assembly.GetName().Name + "\"", StringComparison.Ordinal),
                Assembly.GetExecutingAssembly(),
                uri: ThemeResourceUri) is not Styles styles)
        {
            throw new InvalidOperationException("The embedded Notch Style theme is not a Styles resource.");
        }

        Add(styles);
    }
}

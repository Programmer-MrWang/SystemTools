using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.IO;
using System.Reflection;

namespace SystemTools.Themes.NotchStyle;

public sealed class NotchStyleStyles : Styles
{
    private static readonly Uri ThemeResourceUri =
        new("avares://SystemTools/Themes/NotchStyle/Theme.axaml.txt");

    public NotchStyleStyles()
    {
        using var stream = AssetLoader.Open(ThemeResourceUri);
        using var reader = new StreamReader(stream);
        if (AvaloniaRuntimeXamlLoader.Load(
                reader.ReadToEnd(),
                Assembly.GetExecutingAssembly(),
                uri: ThemeResourceUri) is not Styles styles)
        {
            throw new InvalidOperationException("The embedded Notch Style theme is not a Styles resource.");
        }

        Add(styles);
    }
}

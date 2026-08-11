using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.IO;
using System.Linq;

namespace SystemTools.Themes.CardTypeComponent;

public sealed class CardTypeComponentStyles : Styles
{
    private static readonly Uri ThemeResourceUri =
        new("avares://SystemTools/Themes/CardTypeComponent/Theme.axaml.txt");

    public CardTypeComponentStyles()
    {
        var classIslandAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "ClassIsland", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("ClassIsland host assembly is not loaded.");

        using var stream = AssetLoader.Open(ThemeResourceUri);
        using var reader = new StreamReader(stream);
        if (AvaloniaRuntimeXamlLoader.Load(reader.ReadToEnd(), classIslandAssembly, uri: ThemeResourceUri)
            is not Styles styles)
        {
            throw new InvalidOperationException("The embedded card-type component theme is not a Styles resource.");
        }

        Add(styles);
    }
}

using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.IO;
using System.Linq;

namespace SystemTools.Themes.ClassWidgets;

public sealed class ClassWidgetsStyles : Styles
{
    private static readonly Uri ThemeResourceUri =
        new("avares://SystemTools/Themes/ClassWidgets/Theme.axaml.txt");

    public ClassWidgetsStyles()
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
            throw new InvalidOperationException("The embedded ClassWidgets theme is not a Styles resource.");
        }

        // Keep the plugin control out of the host-assembly runtime XAML and expose
        // it through a template created in the plugin load context. The card binds
        // to its owning ComponentPresenter once it enters the host tree.
        var cardTemplate = new FuncDataTemplate<object?>((_, _) => new ClassWidgetsCard());
        // Register on both levels: the outer style is what ClassIsland adds
        // to RootStyles, while the inner style owns the parsed theme rules.
        Resources["ClassWidgets.CardTemplate"] = cardTemplate;
        styles.Resources["ClassWidgets.CardTemplate"] = cardTemplate;

        Add(styles);
    }
}

using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools;

public sealed class NativeWallpaperSettingsControl : ActionSettingsControlBase<ChangeWallpaperSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var mode = new ComboBox { ItemsSource = new[] { "图片", "纯色" }, SelectedIndex = (int)Settings.Mode };
        mode.SelectionChanged += (_, _) => Settings.Mode = (ChangeWallpaperMode)mode.SelectedIndex;
        var path = new TextBox { PlaceholderText = "壁纸图片的完整路径" };
        path.Bind(TextBox.TextProperty, new Binding(nameof(Settings.ImagePath)) { Source = Settings, Mode = BindingMode.TwoWay });
        var color = new TextBox { PlaceholderText = "#RRGGBB 或 R,G,B" };
        color.Bind(TextBox.TextProperty, new Binding(nameof(Settings.SolidColor)) { Source = Settings, Mode = BindingMode.TwoWay });
        var fit = new ComboBox { ItemsSource = new[] { "平铺", "居中", "拉伸", "填充", "适应", "跨屏" }, SelectedIndex = Settings.FitStyle };
        fit.SelectionChanged += (_, _) => Settings.FitStyle = fit.SelectedIndex;
        Content = new StackPanel { Spacing = 10, Children = { mode, path, color, fit,
            new TextBlock { Text = "平台不支持的布局会明确提示。恢复操作会保留用户随后更换的壁纸。", TextWrapping = Avalonia.Media.TextWrapping.Wrap } } };
    }
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace SystemTools;

[SettingsPageInfo("SystemTools.M1.Diagnostics", "SystemTools M1 诊断")]
public sealed class DiagnosticsSettingsPage : SettingsPageBase
{
    public DiagnosticsSettingsPage(DiagnosticService diagnostics, SystemTools.Contracts.FeatureCatalog catalog)
    {
        var status = new TextBlock { Text = diagnostics.Summary, TextWrapping = TextWrapping.Wrap };
        var button = new Button { Content = "记录平台诊断", HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        {
            try { diagnostics.Capture("settings-button"); status.Text = diagnostics.Summary + "\n诊断已保存。"; }
            catch (Exception ex) { status.Text = "保存失败：" + ex.Message; }
        };
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 16,
            Children =
            {
                new TextBlock { Text = "SystemTools · 平台诊断", FontSize = 24 },
                new TextBlock { Text = "Windows 业务已接入；其他平台当前提供构建与加载诊断。", TextWrapping = TextWrapping.Wrap },
                status, button,
                new TextBox { Text = string.Join("\n", catalog.Snapshot().Select(x => $"{x.Feature.Name}：{x.Capability.State} {x.Capability.Reason}")), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "此按钮仅保存平台诊断。Windows 设置与原有功能请使用 SystemTools 设置页。", TextWrapping = TextWrapping.Wrap }
            }
        };
    }
}

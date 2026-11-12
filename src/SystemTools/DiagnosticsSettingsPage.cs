using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace SystemTools;

[SettingsPageInfo("SystemTools.M1.Diagnostics", "SystemTools M1 诊断")]
public sealed class DiagnosticsSettingsPage : SettingsPageBase
{
    public DiagnosticsSettingsPage(DiagnosticService diagnostics)
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
                new TextBlock { Text = "SystemTools · M1 加载预览", FontSize = 24 },
                new TextBlock { Text = "当前仅验证平台构建与加载。完整 Windows 功能将在 M2 接入。", TextWrapping = TextWrapping.Wrap },
                status, button,
                new TextBlock { Text = "已有配置在此阶段保持原样。此操作只在插件配置目录保存 M1 诊断文件。", TextWrapping = TextWrapping.Wrap }
            }
        };
    }
}

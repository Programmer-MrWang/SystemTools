using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;

namespace SystemTools;

[SettingsPageInfo("SystemTools.M1.Diagnostics", "SystemTools 平台支持状态")]
public sealed class DiagnosticsSettingsPage : SettingsPageBase
{
    public DiagnosticsSettingsPage(DiagnosticService diagnostics, FeatureCatalog catalog)
    {
        var status = new TextBlock { Text = diagnostics.Summary, TextWrapping = TextWrapping.Wrap };
        var features = new TextBox
        {
            Text = FormatFeatures(catalog),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 360
        };
        var refresh = new Button { Content = "刷新状态", HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) =>
        {
            status.Text = diagnostics.Summary;
            features.Text = FormatFeatures(catalog);
        };
        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "SystemTools · 平台支持状态", FontSize = 24 },
                new TextBlock { Text = "当前运行平台与功能状态如下。设置、权限、设备或依赖不满足时，会显示对应原因。", TextWrapping = TextWrapping.Wrap },
                status,
                refresh,
                new TextBlock { Text = "功能状态", FontWeight = FontWeight.SemiBold },
                features
            }
        };
        if (InternalDiagnostics.Enabled)
        {
            var export = new Button { Content = "导出平台诊断", HorizontalAlignment = HorizontalAlignment.Left };
            export.Click += (_, _) =>
            {
                try
                {
                    diagnostics.Capture("settings-button");
                    status.Text = diagnostics.Summary + "\n诊断信息已导出。";
                }
                catch (Exception ex)
                {
                    status.Text = "导出失败：" + ex.Message;
                }
            };
            panel.Children.Insert(4, export);
        }
        Content = panel;
    }

    internal static string FormatFeatures(FeatureCatalog catalog) =>
        string.Join(Environment.NewLine, catalog.Snapshot().Select(item =>
        {
            var reason = string.IsNullOrWhiteSpace(item.Capability.Reason) ? "" : "：" + item.Capability.Reason;
            return $"{item.Feature.Name} — {StateText(item.Capability.State)}{reason}";
        }));

    internal static string StateText(CapabilityState state) => state switch
    {
        CapabilityState.Available => "可用",
        CapabilityState.Disabled => "需启用",
        CapabilityState.MissingDependency => "缺少依赖",
        CapabilityState.PermissionRequired => "需要授权",
        CapabilityState.MissingDevice => "未检测到设备",
        CapabilityState.RestartRequired => "需要重启",
        _ => "当前平台不支持"
    };
}

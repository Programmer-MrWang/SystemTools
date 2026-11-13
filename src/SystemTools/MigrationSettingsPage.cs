using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Common.Migration;

namespace SystemTools;

[SettingsPageInfo("SystemTools.Migration", "旧版本数据导入")]
public sealed class MigrationSettingsPage : SettingsPageBase
{
    public MigrationSettingsPage(MigrationLocation location)
    {
        var migration = new IdentityMigration();
        var source = new TextBox { Text = Path.Combine(Path.GetDirectoryName(location.ConfigFolder)!, "SystemTools-Cross-platform"), PlaceholderText = "旧跨平台版配置目录" };
        var output = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var rows = new StackPanel { Spacing = 8 };
        var previewButton = new Button { Content = "扫描并预览" };
        var applyButton = new Button { Content = "导入所选配置", IsEnabled = false };
        var choices = new List<(MigrationPreview Preview, ComboBox Choice)>();
        previewButton.Click += (_, _) =>
        {
            try
            {
                choices.Clear(); rows.Children.Clear(); applyButton.IsEnabled = false;
                var folder = Path.GetFullPath(source.Text ?? "");
                IdentityMigration.RejectLinks(folder);
                foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(folder, file);
                    if (relative.Split(Path.DirectorySeparatorChar).Any(x => x.StartsWith('.')) || relative.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    var preview = migration.Preview(new(file, Path.Combine(location.ConfigFolder, relative)));
                    var choice = new ComboBox { ItemsSource = new[] { "保留现有", "导入旧版" }, SelectedIndex = preview.Conflict ? 0 : 1 };
                    choices.Add((preview, choice));
                    rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                        Children = { new TextBlock { Text = $"{relative} · {(preview.Conflict ? "存在冲突" : "可导入")} · {preview.ConvertedReferences} 个引用转换" }, choice } });
                }
                output.Text = $"已扫描 {choices.Count} 个配置文件。冲突项默认保留现有文件；导入前将备份目标，来源文件保留。宿主自动化、组件和授权配置请退出宿主后使用迁移工具的显式文件计划。";
                applyButton.IsEnabled = choices.Count > 0;
            }
            catch (Exception ex) { output.Text = "扫描失败，未改动数据。异常类型：" + ex.GetType().Name; }
        };
        applyButton.Click += async (_, _) =>
        {
            applyButton.IsEnabled = false;
            previewButton.IsEnabled = false;
            try
            {
                var selected = choices.Select(x => (x.Preview, x.Choice.SelectedIndex == 1 ? ImportChoice.ImportLegacy : ImportChoice.KeepExisting)).ToArray();
                var journal = await Task.Run(() => migration.Apply(location.ConfigFolder, selected));
                output.Text = "导入完成，请立即重启 ClassIsland 使新配置生效。数据回退日志：" + journal;
            }
            catch (Exception ex) { output.Text = "导入未完成，原数据与迁移日志已保留。请查看日志恢复；异常类型：" + ex.GetType().Name; previewButton.IsEnabled = true; }
        };
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 16,
            Children = { new TextBlock { Text = "旧跨平台版数据导入", FontSize = 24 },
                new TextBlock { Text = "选择来源、预览冲突，再导入需要的配置。操作完成后重启应用。", TextWrapping = TextWrapping.Wrap }, source, previewButton, rows, applyButton, output } } };
    }
}
public sealed record MigrationLocation(string ConfigFolder);

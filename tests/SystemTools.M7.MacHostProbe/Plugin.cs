using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums;
using Microsoft.Extensions.Hosting;

namespace SystemTools.M7.MacHostProbe;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        var root = Environment.GetEnvironmentVariable("SystemTools_M7_ProbeRoot") ?? throw new InvalidOperationException("需要隔离验收目录。");
        var expected = Environment.GetEnvironmentVariable("SystemTools_M7_Expected") ?? "Loaded";
        AppBase.Current.AppStarted += async (_, _) =>
        {
            var checks = new List<string>(); var failures = new List<string>();
            void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
            try
            {
                var loaded = IPluginService.LoadedPlugins.SingleOrDefault(x => x.Manifest.Id == "SystemTools");
                if (expected == "Absent") Check(loaded is null, "宿主卸载后不再发现 SystemTools 插件");
                else
                {
                    Check(loaded is not null && loaded.LoadStatus.ToString() == expected, "原版宿主插件状态：" + loaded?.LoadStatus + "，预期：" + expected);
                    if (expected == "Error") Check(loaded!.Exception?.ToString().Contains("SystemTools-Cross-platform", StringComparison.Ordinal) == true, "旧预览身份冲突明确阻止正式插件重复注册");
                    if (expected == "Loaded")
                    {
                        Check(loaded!.Manifest.Version == "3.0.0.0", "正式插件身份与数字版本保持");
                        var mode = Environment.GetEnvironmentVariable("SystemTools_M7_PackageKind") ?? "M7";
                        if (mode == "M7")
                        {
                            var type = Type.GetType("SystemTools.Contracts.FeatureCatalog, SystemTools.Contracts", throwOnError: true)!;
                            var catalog = ClassIsland.Shared.IAppHost.Host!.Services.GetService(type) ?? throw new Exception("未注册功能目录");
                            var rows = ((System.Collections.IEnumerable)type.GetMethod("Snapshot")!.Invoke(catalog, null)!).Cast<object>().ToArray();
                            Check(rows.Length == 82, "最终候选包运行时功能台账 82 项");
                            Check(Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("ClassIsland_PackageRoot")!, "data/Config/Plugins/SystemTools/.upgrade-snapshots/3.0.0.0")), "首次加载在写入配置前建立升级快照");
                            var raster = Type.GetType("SystemTools.NativeRasterImage, SystemTools.SharedHost", true)!;
                            var png = Path.Combine(root, "runtime.png");
                            raster.GetMethod("WriteSolid")!.Invoke(null, [png, 8, 8, (byte)20, (byte)40, (byte)60]);
                            var appearance = Type.GetType("SystemTools.NativeDesktopAppearance, SystemTools.SharedHost", true)!;
                            var color = appearance.GetMethod("ReadColor")!.Invoke(null, [png])!;
                            Check((byte)color.GetType().GetProperty("R")!.GetValue(color)! == 20, "真实宿主使用最终候选的 Skia PNG 编解码");
                            dynamic workbook = Activator.CreateInstance(AssemblyLoadContext.GetLoadContext(raster.Assembly)!.LoadFromAssemblyName(new AssemblyName("NPOI.OOXML")).GetType("NPOI.XSSF.UserModel.XSSFWorkbook", true)!)!;
                            try
                            {
                                workbook.CreateSheet("候选验收").CreateRow(0).CreateCell(0).SetCellValue("中文课表");
                                using var output = new MemoryStream(); workbook.Write(output, true);
                                Check(output.Length > 100, "真实宿主运行自建 NPOI 并写出 XLSX");
                            }
                            finally { ((IDisposable)workbook).Dispose(); }
                            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "SixLabors.ImageSharp"), "最终插件未引入旧 ImageSharp 程序集");
                        }
                    }
                }
                var config = Path.Combine(Environment.GetEnvironmentVariable("ClassIsland_PackageRoot")!, "data/Config/Plugins/SystemTools/Main.json");
                using var document = JsonDocument.Parse(File.ReadAllText(config));
                Check(document.RootElement.GetProperty("m7Unknown").GetProperty("keep").GetInt32() == 17, "宿主启动后保留未知配置字段");
                Check(document.RootElement.GetProperty("aiApiKey").GetString() == "dpapi:opaque-test", "宿主启动后不改写不属于本平台的密文");
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "acceptance.json"), JsonSerializer.Serialize(new { passed = failures.Count == 0, checks, failures, expected, fullPlatformAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
                await Dispatcher.UIThread.InvokeAsync(() => AppBase.Current.Stop());
            }
        };
    }
}

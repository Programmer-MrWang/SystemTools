using SystemTools.Contracts;

var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine($"PASS {++checks}: {message}"); }
var catalog = new FeatureCatalog("linux-x64");
Check(!catalog.CanCreate("SystemTools.Copy"), "未接入实现不出现在新建入口");
Check(catalog.CanCreate("OtherPlugin.Action"), "不干涉其他插件功能");
var enabled = true;
catalog.Bind("SystemTools.Copy", () => new(enabled ? CapabilityState.Available : CapabilityState.Disabled));
Check(catalog.CanCreate("SystemTools.Copy"), "统一目录开放已接入功能");
enabled = false;
Check(!catalog.CanCreate("SystemTools.Copy"), "能力每次使用重新检查");
try { catalog.EnsureExecutable("SystemTools.Copy"); throw new Exception("静默成功"); } catch (InvalidOperationException) { Check(true, "不可用旧行动明确失败"); }
Check(catalog.Snapshot().Select(x => x.Feature.Id).Distinct().Count() == catalog.Snapshot().Count, "稳定 ID 唯一");
Console.WriteLine($"M3 checks: {checks}");

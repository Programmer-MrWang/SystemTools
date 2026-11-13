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
var temporary = Path.Combine(Path.GetTempPath(), "systemtools-m3-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var path = Path.Combine(temporary, "Main.json");
    File.WriteAllText(path, """{"Known":{"Value":1,"future":{"x":3}},"Items":[{"Id":"a","Value":2,"future":4},{"Id":"b","Value":3,"future":5}],"futureRoot":6}""");
    var first = new SystemTools.Common.Storage.AtomicJsonStore<TestDocument>(path);
    var value = first.Load();
    var stale = new SystemTools.Common.Storage.AtomicJsonStore<TestDocument>(path);
    var staleValue = stale.Load();
    value.Known.Value = 9;
    value.Items.Reverse();
    first.Save(value);
    var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
    Check(json["Known"]!["future"]!["x"]!.GetValue<int>() == 3 && json["futureRoot"]!.GetValue<int>() == 6, "主配置及嵌套未知字段保留");
    Check(json["Items"]![0]!["Id"]!.GetValue<string>() == "b" && json["Items"]![0]!["future"]!.GetValue<int>() == 5, "稳定身份数组重排保留未知字段");
    try { stale.Save(staleValue); throw new Exception("并发覆盖"); } catch (SystemTools.Common.Storage.ConfigurationConflictException) { Check(true, "拒绝过期写入者"); }
    Check(File.Exists(path + ".bak") && json["_systemToolsSchema"]!.GetValue<int>() == 1, "保存版本和迁移前备份");
    File.WriteAllText(path, "{broken");
    var recovered = new SystemTools.Common.Storage.AtomicJsonStore<TestDocument>(path);
    var recoveredValue = recovered.Load();
    Check(recovered.RecoveryNotice is not null && Directory.GetFiles(temporary, "*.corrupt-*").Length == 1, "损坏配置备份恢复且原文另存");
    recovered.Save(recoveredValue);
    Check(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) is not null, "恢复后安全保存");
    File.WriteAllText(path, """{"_systemToolsSchema":999}""");
    try { new SystemTools.Common.Storage.AtomicJsonStore<TestDocument>(path).Load(); throw new Exception("未来版本降级"); } catch (InvalidDataException) { Check(true, "拒绝覆写未来版本配置"); }
    var source = Path.Combine(temporary, "Cache");
    var destination = Path.Combine(temporary, "Profiles");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "Default.json"), "{}");
    var report = SystemTools.Common.Storage.PersistentProfileImport.CopyOnce(source, destination);
    Check(report.Imported.Length == 1 && File.Exists(Path.Combine(source, "Default.json")), "缓存方案复制到持久目录且保留来源");
    File.Delete(Path.Combine(destination, "Default.json"));
    Check(SystemTools.Common.Storage.PersistentProfileImport.CopyOnce(source, destination).Imported.Length == 0, "删除已迁移方案后不自动重建");
    File.WriteAllText(Path.Combine(source, "Conflict.json"), "{\"a\":1}");
    File.WriteAllText(Path.Combine(destination, "Conflict.json"), "{\"a\":2}");
    Check(SystemTools.Common.Storage.PersistentProfileImport.CopyOnce(source, destination).Conflicts.SequenceEqual(["Conflict.json"]), "方案冲突保留两端并报告");
}
finally { Directory.Delete(temporary, recursive: true); }
Console.WriteLine($"M3 checks: {checks}");

public sealed class TestDocument
{
    public TestItem Known { get; set; } = new();
    public List<TestItem> Items { get; set; } = [];
}
public sealed class TestItem
{
    public string Id { get; set; } = "";
    public int Value { get; set; }
}

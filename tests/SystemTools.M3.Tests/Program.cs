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
    var migration = new SystemTools.Common.Migration.IdentityMigration();
    var migrationSource = Path.Combine(temporary, "legacy.json");
    var migrationTarget = Path.Combine(temporary, "formal.json");
    File.WriteAllText(migrationSource, """{"enabledActions":{"SystemTools.CrossPlatform.Copy":true},"Action":{"Id":"SystemTools.CrossPlatform.Copy"},"userText":"SystemTools.CrossPlatform.Copy","aiApiKey":"dpapi-current-user:v1:REDACTED","unknown":{"keep":1}}""");
    File.WriteAllText(migrationTarget, """{"original":true}""");
    var preview = migration.Preview(new(migrationSource, migrationTarget));
    var candidate = System.Text.Json.Nodes.JsonNode.Parse(preview.Candidate)!;
    Check(preview.Conflict && preview.ConvertedReferences == 2, "旧身份迁移预览冲突与引用数量");
    Check(candidate["userText"]!.GetValue<string>() == "SystemTools.CrossPlatform.Copy" && candidate["aiApiKey"]!.GetValue<string>() == "dpapi-current-user:v1:REDACTED", "迁移保留普通文本和不可解密密文");
    Check(candidate["enabledActions"]!["SystemTools.Copy"]!.GetValue<bool>() && candidate["Action"]!["Id"]!.GetValue<string>() == "SystemTools.Copy", "结构化转换启用字典和宿主身份字段");
    var journal = migration.Apply(temporary, [(preview, SystemTools.Common.Migration.ImportChoice.ImportLegacy)]);
    Check(File.ReadAllText(migrationTarget) == preview.Candidate && File.Exists(migrationSource), "迁移提交并保留来源");
    SystemTools.Common.Migration.IdentityMigration.Resume(journal);
    Check(File.ReadAllText(migrationTarget) == preview.Candidate, "迁移恢复重复执行幂等");
    Check(migration.Preview(new(migrationSource, migrationTarget)).Conflict == false, "重复导入不产生语义冲突");
    SystemTools.Common.Migration.IdentityMigration.Rollback(journal);
    Check(File.ReadAllText(migrationTarget) == "{\"original\":true}", "迁移回退恢复目标原文");
    SystemTools.Common.Migration.IdentityMigration.Rollback(journal);
    Check(File.ReadAllText(migrationTarget) == "{\"original\":true}", "迁移回退可重复执行");

    var source2 = Path.Combine(temporary, "legacy2.json");
    var target2 = Path.Combine(temporary, "formal2.json");
    File.WriteAllText(source2, "{\"id\":\"SystemTools.CrossPlatform.Delete\"}");
    var resumeJournal = migration.Apply(temporary, [(migration.Preview(new(migrationSource, migrationTarget)), SystemTools.Common.Migration.ImportChoice.ImportLegacy),
        (migration.Preview(new(source2, target2)), SystemTools.Common.Migration.ImportChoice.ImportLegacy)]);
    var resumeState = System.Text.Json.JsonSerializer.Deserialize<SystemTools.Common.Migration.MigrationJournal>(File.ReadAllText(resumeJournal))!;
    File.Delete(target2);
    resumeState.Status = "Prepared";
    File.WriteAllText(resumeJournal, System.Text.Json.JsonSerializer.Serialize(resumeState));
    SystemTools.Common.Migration.IdentityMigration.Resume(resumeJournal);
    Check(File.Exists(target2), "迁移日志恢复中途缺失的后续写入");
    File.WriteAllText(migrationTarget, "{\"editedAfterMigration\":true}");
    try { SystemTools.Common.Migration.IdentityMigration.Rollback(resumeJournal); throw new Exception("回退覆盖新数据"); }
    catch (SystemTools.Common.Storage.ConfigurationConflictException) { Check(true, "回退拒绝覆盖迁移后修改"); }
    File.WriteAllBytes(migrationTarget, new byte[] { 0xef, 0xbb, 0xbf }.Concat(System.Text.Encoding.UTF8.GetBytes("{}")).ToArray());
    var bomOriginal = File.ReadAllBytes(migrationTarget);
    var bomJournal = migration.Apply(temporary, [(migration.Preview(new(migrationSource, migrationTarget)), SystemTools.Common.Migration.ImportChoice.ImportLegacy)]);
    SystemTools.Common.Migration.IdentityMigration.Rollback(bomJournal);
    Check(File.ReadAllBytes(migrationTarget).SequenceEqual(bomOriginal), "回退逐字节恢复含 BOM 的配置");
    var stalePreview = migration.Preview(new(migrationSource, migrationTarget));
    File.WriteAllText(migrationTarget, "{}");
    try { migration.Apply(temporary, [(stalePreview, SystemTools.Common.Migration.ImportChoice.ImportLegacy)]); throw new Exception("预览失效仍覆盖"); }
    catch (SystemTools.Common.Storage.ConfigurationConflictException) { Check(true, "迁移拒绝预览后发生的修改"); }
    try { migration.Apply(Path.Combine(temporary, "other"), [(migration.Preview(new(migrationSource, migrationTarget)), SystemTools.Common.Migration.ImportChoice.ImportLegacy)]); throw new Exception("越界迁移"); }
    catch (IOException) { Check(true, "迁移写入严格限制在指定目录"); }

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
var unavailable = new UnavailableCredentialProtector("keychain:v1:", "未接入系统凭据后端");
Check(!unavailable.TryUnprotect("dpapi-current-user:v1:REDACTED", out var plaintext) && plaintext == "", "无凭据后端时不伪造解密结果");
try { unavailable.Protect("test-secret"); throw new Exception("不安全降级"); } catch (PlatformNotSupportedException) { Check(true, "无凭据后端时拒绝明文降级"); }
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

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
var needsRestart = false;
catalog.BindForRegistration("SystemTools.Move", () => new(needsRestart ? CapabilityState.Available : CapabilityState.Disabled));
needsRestart = true;
Check(catalog.Get("SystemTools.Move").State == CapabilityState.RestartRequired && !catalog.CanCreate("SystemTools.Move"), "初始未注册功能就绪后明确要求重启");
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
    var identitySource = Path.Combine(temporary, "identity.json");
    File.WriteAllText(identitySource, """{"selectedThemeId":"SystemTools.CrossPlatform.notch-style","channelId":"4bee12e4-ab5a-4193-8e8e-1651c23228f3","componentId":"e6fc9a28-a104-50f2-95e3-b237f9cc4dac","userText":"SystemTools.CrossPlatform.notch-style"}""");
    var mappedIdentities = System.Text.Json.Nodes.JsonNode.Parse(migration.Preview(new(identitySource, Path.Combine(temporary, "identity-target.json"))).Candidate)!;
    Check(mappedIdentities["selectedThemeId"]!.GetValue<string>() == "notch-style" && mappedIdentities["channelId"]!.GetValue<string>() == "7D7EFBF1-02A4-4A15-9C1A-2229027339B2", "主题选择及通知渠道采用显式身份映射");
    Check(mappedIdentities["componentId"]!.GetValue<string>() == "A7C3455E-6A4E-4D4D-9D0D-7C6FCB5E1E3A" && mappedIdentities["userText"]!.GetValue<string>() == "SystemTools.CrossPlatform.notch-style", "组件 GUID 映射忽略大小写且普通文本不变");
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
    using (var documentLock = new FileStream(target2 + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    {
        try { SystemTools.Common.Migration.IdentityMigration.Resume(resumeJournal); throw new Exception("迁移忽略文档锁"); }
        catch (IOException) { Check(!File.Exists(target2), "迁移与普通保存共享文档锁"); }
    }
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

    var fileOperations = new SystemTools.Common.Files.FileOperations();
    var fileSource = Path.Combine(temporary, "source.txt");
    var fileCopy = Path.Combine(temporary, "copy.txt");
    var fileMove = Path.Combine(temporary, "move.txt");
    File.WriteAllText(fileSource, "file-content");
    await fileOperations.TransferAsync(fileSource, fileCopy, directory: false, move: false);
    await fileOperations.TransferAsync(fileCopy, fileMove, directory: false, move: true);
    Check(File.ReadAllText(fileMove) == "file-content" && !File.Exists(fileCopy), "共享文件复制和移动");
    await fileOperations.DeleteAsync(fileMove, directory: false);
    Check(!File.Exists(fileMove), "共享文件删除");
    var tree = Path.Combine(temporary, "tree");
    Directory.CreateDirectory(Path.Combine(tree, "nested"));
    File.WriteAllText(Path.Combine(tree, "nested", "keep.txt"), "tree-content");
    var copyParent = Path.Combine(temporary, "copyParent");
    await fileOperations.TransferAsync(tree, copyParent, directory: true, move: false);
    Check(File.ReadAllText(Path.Combine(copyParent, "tree", "nested", "keep.txt")) == "tree-content", "共享目录递归复制");
    File.WriteAllText(Path.Combine(copyParent, "tree", "old.txt"), "old");
    await fileOperations.TransferAsync(tree, copyParent, directory: true, move: false);
    Check(!File.Exists(Path.Combine(copyParent, "tree", "old.txt")), "目录复制保留原版整体覆盖语义");
    try { await fileOperations.TransferAsync(tree, Path.Combine(tree, "child"), true, false); throw new Exception("包含路径可写"); }
    catch (IOException) { Check(true, "拒绝源目标相互包含"); }
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try { await fileOperations.TransferAsync(tree, copyParent, true, false, cancelled.Token); throw new Exception("中断无效"); }
        catch (OperationCanceledException) { Check(File.Exists(Path.Combine(copyParent, "tree", "nested", "keep.txt")), "中断前不破坏目标"); }
    }
    var failing = new SystemTools.Common.Files.FileOperations(new FailingDirectoryCopier());
    try { await failing.TransferAsync(tree, copyParent, true, false); throw new Exception("故障未传播"); }
    catch (IOException) { Check(File.Exists(Path.Combine(copyParent, "tree", "nested", "keep.txt")) && Directory.Exists(tree), "复制故障保留源和旧目标"); }
    var moveParent = Path.Combine(temporary, "moveParent");
    await fileOperations.TransferAsync(Path.Combine(copyParent, "tree"), moveParent, true, true);
    Check(!Directory.Exists(Path.Combine(copyParent, "tree")) && Directory.Exists(Path.Combine(moveParent, "tree")), "目录移动提交后移除源");
    await fileOperations.DeleteAsync(Path.Combine(moveParent, "tree"), true);
    Check(!Directory.Exists(Path.Combine(moveParent, "tree")), "目录删除通过托管 API 执行");
    var snapshots = new SystemTools.Common.Automation.ActionRestoreStore<bool>();
    var groupA = Guid.NewGuid(); var groupB = Guid.NewGuid(); var ownerA = new object(); var ownerB = new object();
    snapshots.Capture(groupA, ownerA, "target", false);
    snapshots.Capture(groupB, ownerB, "target", true);
    Check(!snapshots.TryRestore(groupA, ownerA, out _), "先恢复较早组不覆盖后续组状态");
    Check(snapshots.TryRestore(groupB, ownerB, out var originalState) && !originalState, "后续组恢复到最初状态");
    var firstOwner = new object(); var secondOwner = new object();
    snapshots.Capture(groupA, firstOwner, "shared-target", false);
    snapshots.Capture(groupA, secondOwner, "shared-target", true);
    snapshots.Capture(groupA, firstOwner, "shared-target", true);
    Check(!snapshots.TryRestore(groupA, firstOwner, out _), "同组多行动恢复时保留其他持有者");
    Check(snapshots.TryRestore(groupA, secondOwner, out var groupOriginal) && !groupOriginal, "同组重复执行保留首次触发状态");
    var idA = Guid.NewGuid(); var idB = Guid.NewGuid();
    var workflows = new[] { new TestWorkflow(idB, "same"), new TestWorkflow(idA, "same") };
    Check(SystemTools.Common.Automation.WorkflowTargetResolver.Resolve(workflows, idA, "same", 0, x => x.Id, x => x.Name)?.Id == idA, "工作流重排仍按稳定身份解析");
    Check(SystemTools.Common.Automation.WorkflowTargetResolver.Resolve(workflows, null, "same", 0, x => x.Id, x => x.Name) is null, "工作流重名时拒绝猜测目标");

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
    File.WriteAllText(Path.Combine(source, "Damaged.json"), "{damaged");
    var invalidReport = SystemTools.Common.Storage.PersistentProfileImport.CopyOnce(source, destination);
    Check(invalidReport.Invalid.SequenceEqual(["Damaged.json"]) && !File.Exists(Path.Combine(destination, "Damaged.json")), "旧方案损坏时保留来源并报告而不阻止其他方案");
}
finally { Directory.Delete(temporary, recursive: true); }
var cleanupOrder = new List<int>();
var lifetime = new SystemTools.Common.Lifecycle.LifetimeScope();
lifetime.Own(() => cleanupOrder.Add(1));
lifetime.Own(() => throw new IOException("清理故障注入"));
lifetime.Own(() => cleanupOrder.Add(3));
Check(lifetime.Close().Count == 1 && cleanupOrder.SequenceEqual([3, 1]), "资源逆序清理且单项失败不跳过后续项");
Check(lifetime.Close().Count == 0 && cleanupOrder.Count == 2, "重复停止只清理一次");
try { lifetime.Own(() => { }); throw new Exception("关闭后注册"); } catch (ObjectDisposedException) { Check(true, "关闭后不接受新资源"); }
var layout = SystemTools.Common.Layout.FloatingWindowRows.WithFallback([["a", "missing"]], ["a", "b", "c"], new HashSet<string> { "c" });
Check(layout.Count == 1 && layout[0].SequenceEqual(["a", "b"]), "布局仅计算可见按钮并保留原配置");
if (OperatingSystem.IsWindows())
{
    var command = await SystemTools.Common.Processes.CommandRunner.RunAsync("cmd.exe", ["/d", "/c", "echo output & echo error 1>&2"]);
    Check(command.ExitCode == 0 && command.StandardOutput.Contains("output") && command.StandardError.Contains("error"), "异步命令同时读取输出和错误流");
    try { await SystemTools.Common.Processes.CommandRunner.RunAsync("ping.exe", ["-n", "20", "127.0.0.1"], timeout: TimeSpan.FromMilliseconds(100)); throw new Exception("命令无超时"); }
    catch (TimeoutException) { Check(true, "命令超时终止子进程并排空输出"); }
}
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

public sealed class FailingDirectoryCopier : SystemTools.Common.Files.IDirectoryCopyBackend
{
    public Task CopyAsync(string source, string target, CancellationToken token) { Directory.CreateDirectory(target); throw new IOException("故障注入"); }
}
public sealed record TestWorkflow(Guid Id, string Name);

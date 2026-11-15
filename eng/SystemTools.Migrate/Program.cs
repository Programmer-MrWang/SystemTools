using System.Diagnostics;
using System.Text.Json;
using SystemTools.Common.Migration;

if (args.Length == 2 && args[0] is "resume" or "rollback")
{
    RequireStopped();
    if (args[0] == "resume") IdentityMigration.Resume(args[1]); else IdentityMigration.Rollback(args[1]);
    Console.WriteLine("迁移日志操作完成。");
    return;
}
if (args.Length != 2 || args[0] is not ("preview" or "apply"))
    throw new ArgumentException("用法：preview <计划.json> 生成不可覆盖的 .reviewed.json；apply <已审阅.reviewed.json>；resume|rollback <journal.json>。操作前退出宿主。");
IdentityMigration.RejectLinks(args[1]);
var migrator = new IdentityMigration();
if (args[0] == "preview")
{
    var plan = JsonSerializer.Deserialize<ImportPlan>(File.ReadAllText(args[1])) ?? throw new InvalidDataException("迁移计划为空。");
    var items = plan.Files.Select(file => new ReviewedFile(migrator.Preview(new(file.Source, file.Target)), file.Choice)).ToArray();
    foreach (var item in items)
        Console.WriteLine($"{item.Preview.Request.Target}：冲突={item.Preview.Conflict}，引用转换={item.Preview.ConvertedReferences}，选择={item.Choice}");
    var reviewed = Path.GetFullPath(args[1]) + ".reviewed.json";
    IdentityMigration.RejectLinks(reviewed);
    using var output = new FileStream(reviewed, FileMode.CreateNew, FileAccess.Write);
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(reviewed, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    JsonSerializer.Serialize(output, new ReviewedPlan(1, Path.GetFullPath(plan.TargetRoot), items), new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine("已审阅计划：" + reviewed);
}
else
{
    RequireStopped();
    var plan = JsonSerializer.Deserialize<ReviewedPlan>(File.ReadAllText(args[1])) ?? throw new InvalidDataException("已审阅计划为空。");
    if (plan.SchemaVersion != 1 || plan.Files is null) throw new InvalidDataException("需要 preview 生成的已审阅计划。");
    Console.WriteLine("迁移日志：" + migrator.Apply(plan.TargetRoot, plan.Files.Select(x => (x.Preview, x.Choice)).ToArray()));
}
static void RequireStopped()
{
    foreach (var process in Process.GetProcesses())
        using (process)
            if (process.ProcessName.Contains("ClassIsland", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Contains("SystemToolsCompanion", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请先退出 ClassIsland 和采集助手，再迁移或回退。");
}
public sealed record ReviewedPlan(int SchemaVersion, string TargetRoot, ReviewedFile[] Files);
public sealed record ReviewedFile(MigrationPreview Preview, ImportChoice Choice);
public sealed class ImportPlan
{
    public string TargetRoot { get; set; } = "";
    public List<ImportFile> Files { get; set; } = [];
}
public sealed class ImportFile
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ImportChoice Choice { get; set; } = ImportChoice.KeepExisting;
}

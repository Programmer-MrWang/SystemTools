using System.Text.Json;
using SystemTools.Common.Migration;

if (args.Length == 2 && args[0] is "resume" or "rollback")
{
    if (args[0] == "resume") IdentityMigration.Resume(args[1]); else IdentityMigration.Rollback(args[1]);
    Console.WriteLine("迁移日志操作完成。");
    return;
}
if (args.Length != 2 || args[0] is not ("preview" or "apply"))
    throw new ArgumentException("用法：preview|apply <计划.json>，或 resume|rollback <journal.json>。宿主相关文件必须在退出宿主后迁移。" );
var plan = JsonSerializer.Deserialize<ImportPlan>(File.ReadAllText(args[1]))!;
var migrator = new IdentityMigration();
var selected = plan.Files.Select(file => (Preview: migrator.Preview(new(file.Source, file.Target)), file.Choice)).ToArray();
foreach (var item in selected)
    Console.WriteLine($"{item.Preview.Request.Target}：冲突={item.Preview.Conflict}，引用转换={item.Preview.ConvertedReferences}，选择={item.Choice}");
if (args[0] == "apply") Console.WriteLine("迁移日志：" + migrator.Apply(plan.TargetRoot, selected));
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

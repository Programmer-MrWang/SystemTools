using NPOI.HSSF.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using SystemTools;
using SystemTools.Common.Migration;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine($"PASS {++checks}: {name}"); }
var root = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "m7"), "systemtools-m7-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var png = Path.Combine(root, "picture.png"); NativeRasterImage.WriteSolid(png, 32, 24, 20, 40, 60);
    foreach (var xlsx in new[] { false, true })
    {
        using IWorkbook workbook = xlsx ? new XSSFWorkbook() : new HSSFWorkbook();
        var sheet = workbook.CreateSheet("中文课表");
        var row = sheet.CreateRow(0); row.CreateCell(0).SetCellValue("语文"); row.CreateCell(1).SetCellValue(42);
        row.CreateCell(2).SetCellFormula("B1*2");
        var font = workbook.CreateFont(); font.IsBold = true;
        var style = workbook.CreateCellStyle(); style.SetFont(font); row.GetCell(0).CellStyle = style;
        var picture = workbook.AddPicture(File.ReadAllBytes(png), PictureType.PNG);
        var anchor = workbook.GetCreationHelper().CreateClientAnchor(); anchor.Col1 = 0; anchor.Row1 = 2;
        sheet.CreateDrawingPatriarch().CreatePicture(anchor, picture).Resize();
        sheet.AutoSizeColumn(0);
        using var output = new MemoryStream(); workbook.Write(output, leaveOpen: true);
        output.Position = 0;
        using var decoded = WorkbookFactory.Create(output);
        Check(decoded.GetSheetAt(0).GetRow(0).GetCell(0).StringCellValue == "语文", $"源码 NPOI {(xlsx ? "XLSX" : "XLS")} 中文与样式读写");
        Check(decoded.GetAllPictures().Count == 1, "源码 NPOI 保留实际 PNG 嵌入图片");
        Check(decoded.GetCreationHelper().CreateFormulaEvaluator().Evaluate(decoded.GetSheetAt(0).GetRow(0).GetCell(2)).NumberValue == 84, "源码 NPOI 公式计算与读取");
    }
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "SixLabors.ImageSharp"), "Excel 与图片处理不加载旧 ImageSharp");

    var folder = Path.Combine(root, "config"); Directory.CreateDirectory(Path.Combine(folder, "Attachments"));
    File.WriteAllText(Path.Combine(folder, "Main.json"), "{\"unknown\":7,\"aiApiKey\":\"dpapi:opaque\"}");
    File.WriteAllBytes(Path.Combine(folder, "Attachments", "data.bin"), [0, 1, 255]);
    foreach (var invalid in new[] { "", ".", "..", "../escape" })
        try { UpgradeSnapshot.Ensure(folder, invalid); throw new Exception("无效版本被接受"); }
        catch (ArgumentException) { Check(true, "拒绝无效快照版本：" + invalid); }
    var snapshot = UpgradeSnapshot.Ensure(folder, "3.0.0.0");
    Check(File.ReadAllText(Path.Combine(snapshot, "files", "Main.json")).Contains("dpapi:opaque"), "升级前快照逐字节保留未知字段和密文");
    Check(File.ReadAllBytes(Path.Combine(snapshot, "files", "Attachments", "data.bin")).SequenceEqual(new byte[] { 0, 1, 255 }), "升级前快照保留附件");
    File.WriteAllText(Path.Combine(folder, "Main.json"), "{\"new\":true}");
    Check(UpgradeSnapshot.Ensure(folder, "3.0.0.0") == snapshot && File.ReadAllText(Path.Combine(snapshot, "files", "Main.json")).Contains("unknown"), "同版本重启不覆盖升级前快照");
    if (!OperatingSystem.IsWindows()) Check((File.GetUnixFileMode(snapshot) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "升级备份仅当前用户可读");
    File.WriteAllText(Path.Combine(snapshot, "files", "Main.json"), "tampered");
    try { UpgradeSnapshot.Ensure(folder, "3.0.0.0"); throw new Exception("损坏快照被接受"); } catch (IOException) { Check(true, "损坏快照阻止后续升级写入"); }
}
finally { Directory.Delete(root, recursive: true); }

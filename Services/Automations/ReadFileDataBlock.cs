using System;
using System.IO;
using System.Threading.Tasks;
using ClassIsland.Core.Icons;
using SuperAutoIsland.Interface.Metadata;
using SuperAutoIsland.Interface.Services;
using SuperAutoIsland.Interface.Services.Automations;
using SystemTools.Settings;

namespace SystemTools.Services.Automations;

public sealed class ReadFileDataBlock : DataBlockBase
{
    public override string Id => "SystemTools.ReadFile";
    public override string Name => "读取文件";
    public override (string, string) Icon => ("文件", FluentIcons.DocumentDataRegular);
    public override string Tooltip => "以 UTF-8 文本读取指定文件。文件无法读取时会报告错误。";
    public override Type SettingsType => typeof(ReadFileSettings);

    public override void GetFields(FieldsRegister it) =>
        it.AddField("filePath", BasicFields.Text("文件路径"));

    public override async Task<object> Handler(object? data)
    {
        if (data is not ReadFileSettings settings || string.IsNullOrWhiteSpace(settings.FilePath))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(settings.FilePath);
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemTools.Models;

/// <summary>
/// ClassIsland 档案（Profile）JSON 的只读映射模型。
/// </summary>
/// <remarks>
/// 本插件不依赖 ClassIsland 的档案相关接口，而是直接读取 <c>Profiles</c> 目录下的档案文件。
/// 这里仅声明导出所需的字段，其余内容一律忽略，因此 ClassIsland 新增字段不会影响解析。
/// </remarks>
public sealed class ClassIslandProfileDocument
{
    public string Name { get; set; } = "";

    public Dictionary<string, ClassIslandTimeLayout> TimeLayouts { get; set; } = new();

    public Dictionary<string, ClassIslandClassPlan> ClassPlans { get; set; } = new();

    public Dictionary<string, ClassIslandClassPlanGroup> ClassPlanGroups { get; set; } = new();

    public Dictionary<string, ClassIslandSubject> Subjects { get; set; } = new();
}

/// <summary>档案中的课表群。</summary>
public sealed class ClassIslandClassPlanGroup
{
    public string Name { get; set; } = "";

    public bool IsGlobal { get; set; }
}

/// <summary>档案中的时间表。</summary>
public sealed class ClassIslandTimeLayout
{
    public string Name { get; set; } = "";

    public bool IsOverlay { get; set; }

    public List<ClassIslandTimeLayoutItem> Layouts { get; set; } = [];
}

/// <summary>时间表中的一个时间点。</summary>
public sealed class ClassIslandTimeLayoutItem
{
    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    /// <summary>0 上课，1 课间，2 分割线，3 行动。</summary>
    public int TimeType { get; set; }

    public string BreakName { get; set; } = "";

    [JsonConverter(typeof(FlexibleGuidConverter))]
    public Guid DefaultClassId { get; set; }
}

/// <summary>档案中的课表。</summary>
public sealed class ClassIslandClassPlan
{
    [JsonConverter(typeof(FlexibleGuidConverter))]
    public Guid TimeLayoutId { get; set; }

    public string Name { get; set; } = "";

    public bool IsEnabled { get; set; } = true;

    public bool IsOverlay { get; set; }

    /// <summary>该课表所属的课表群 ID。</summary>
    [JsonConverter(typeof(FlexibleGuidConverter))]
    public Guid AssociatedGroup { get; set; }

    public ClassIslandTimeRule TimeRule { get; set; } = new();

    public List<ClassIslandClassInfo> Classes { get; set; } = [];
}

/// <summary>课表中的一节课。</summary>
public sealed class ClassIslandClassInfo
{
    [JsonConverter(typeof(FlexibleGuidConverter))]
    public Guid SubjectId { get; set; }

    public bool IsEnabled { get; set; } = true;
}

/// <summary>课表的启用时间规则。</summary>
public sealed class ClassIslandTimeRule
{
    /// <summary>0 每周启用，1 特定日期启用，2 循环启用。</summary>
    public int Type { get; set; }

    /// <summary>每周启用时的星期几，0 为星期日。</summary>
    public int WeekDay { get; set; }

    /// <summary>多周轮换中的第几周，0 表示不轮换。</summary>
    public int WeekCountDiv { get; set; }

    /// <summary>多周轮换的总周数。</summary>
    public int WeekCountDivTotal { get; set; } = 2;

    /// <summary>循环启用时的周期长度（天）。</summary>
    public int LoopCycleDays { get; set; } = 3;

    /// <summary>循环启用时的偏移天数。</summary>
    public int LoopOffsetDays { get; set; }

    // ClassIsland 在序列化时会写出超出 net6.0 目标框架范围的 DateOnly 数据，
    // 这里以字符串接收，避免因格式差异导致整份档案解析失败。
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string RangeStart { get; set; } = "";

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string RangeEnd { get; set; } = "";

    public bool RestrictsEnableRange { get; set; }
}

/// <summary>档案中的科目。</summary>
public sealed class ClassIslandSubject
{
    public string Name { get; set; } = "";

    /// <summary>科目简称。</summary>
    public string Initial { get; set; } = "";

    public string TeacherName { get; set; } = "";

    /// <summary>是否为户外课程。</summary>
    public bool IsOutDoor { get; set; }
}

/// <summary>
/// 将任意 JSON 标量（字符串、数字、日期等）读取为字符串的转换器。
/// </summary>
internal sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Null => "",
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => ""
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>
/// 宽容地读取 <see cref="Guid"/>：空字符串与 null 均视为 <see cref="Guid.Empty"/>。
/// </summary>
/// <remarks>ClassIsland 在部分字段上会写出空字符串，默认转换器会因此而抛出异常。</remarks>
internal sealed class FlexibleGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Guid.Empty;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            return string.IsNullOrWhiteSpace(text) ? Guid.Empty : Guid.Parse(text);
        }

        // 兜底：非字符串的标量（如数字）不做处理。
        reader.Skip();
        return Guid.Empty;
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

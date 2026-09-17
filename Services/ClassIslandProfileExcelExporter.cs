using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.Core;
using Microsoft.Extensions.Logging;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SystemTools.Models;
using SystemTools.Shared;

namespace SystemTools.Services;

/// <summary>可导出的档案内容类别。</summary>
public enum ProfileExportTarget
{
    /// <summary>课表。</summary>
    ClassPlan,

    /// <summary>时间表。</summary>
    TimeLayout,

    /// <summary>科目、简称、教师、户内外。</summary>
    Subject
}

/// <summary>导出选项。</summary>
public sealed record ProfileExcelExportOptions
{
    public bool ExportClassPlan { get; init; }

    public bool ExportTimeLayout { get; init; }

    public bool ExportSubject { get; init; }

    /// <summary>为 <c>true</c> 时导出 .xlsx，否则导出 .xls。</summary>
    public bool UseXlsx { get; init; } = true;

    /// <summary>为 <c>true</c> 时每个内容各存为一个文件，否则合并到一个工作簿。</summary>
    public bool SeparateFiles { get; init; }

    public bool HasAnyTarget => ExportClassPlan || ExportTimeLayout || ExportSubject;

    public int TargetCount =>
        (ExportClassPlan ? 1 : 0) + (ExportTimeLayout ? 1 : 0) + (ExportSubject ? 1 : 0);
}

/// <summary>一个待写出的表格文件。</summary>
public sealed record ProfileExportFile(string FileName, byte[] Content);

/// <summary>工作表中一个独立表格区块。</summary>
internal sealed record ClassPlanBlock(string Title, string[] Header, List<string[]> Rows)
{
    public int ColumnCount => Header.Length;
}

/// <summary>一次导出所依据的档案来源。</summary>
public sealed class ProfileExportSource
{
    public required string FilePath { get; init; }

    public required ClassIslandProfileDocument Profile { get; init; }

    public string ProfileName =>
        string.IsNullOrWhiteSpace(Profile.Name) ? Path.GetFileNameWithoutExtension(FilePath) : Profile.Name;
}

/// <summary>
/// 独立读取 ClassIsland 的 JSON 档案，并将其中的课表、时间表与科目信息导出为 Excel 表格。
/// </summary>
/// <remarks>
/// 本服务不调用 ClassIsland 的档案接口，而是直接定位并解析 <c>Profiles</c> 目录下的档案文件，
/// 因此不受宿主接口变动的影响。导出的样式（字体、列宽、行高、边框、冻结窗格）参照标准课表模板。
/// </remarks>
public sealed class ClassIslandProfileExcelExporter(ILogger<ClassIslandProfileExcelExporter> logger)
{
    private const string ProfilesFolderName = "Profiles";
    private const string SettingsFileName = "Settings.json";
    private const string ManagementProfileFileName = "_management-profile.json";

    /// <summary>列宽，单位为 1/256 字符宽，取自标准模板的约 10.58 字符。</summary>
    private const int StandardColumnWidth = 2709;

    /// <summary>行高（磅），取自标准模板。</summary>
    private const double StandardRowHeightPoints = 25;

    /// <summary>默认课表群 GUID，与 ClassIsland 保持一致。</summary>
    private const string DefaultClassPlanGroupId = "ACAF4EF0-E261-4262-B941-34EA93CB4369";

    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly string[] WeekDayNames = ["星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"];

    /// <summary>定位并读取当前 ClassIsland 档案。</summary>
    /// <exception cref="FileNotFoundException">找不到任何可读取的档案文件。</exception>
    public ProfileExportSource LoadCurrentProfile()
    {
        var profilesFolder = ResolveProfilesFolder();
        var filePath = ResolveCurrentProfileFile(profilesFolder);
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var profile = JsonSerializer.Deserialize<ClassIslandProfileDocument>(json, ProfileJsonOptions)
                      ?? throw new InvalidDataException($"档案 {Path.GetFileName(filePath)} 内容为空或无法解析。");
        logger.LogInformation("[SystemTools]已读取 ClassIsland 档案：{Path}", filePath);
        return new ProfileExportSource { FilePath = filePath, Profile = profile };
    }

    /// <summary>按给定选项生成 Excel 工作簿。</summary>
    public byte[] BuildWorkbook(ProfileExportSource source, ProfileExcelExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        return BuildWorkbook(source, ToTargets(options), options.UseXlsx);
    }

    /// <summary>
    /// 按给定选项生成待写出的文件列表。当选择「多文件」且导出内容达到两项及以上时，
    /// 每项内容各生成一个文件；否则生成单个文件。
    /// </summary>
    public IReadOnlyList<ProfileExportFile> BuildExportFiles(
        ProfileExportSource source,
        ProfileExcelExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var targets = ToTargets(options);
        var extension = options.UseXlsx ? ".xlsx" : ".xls";
        var baseName = BuildSuggestedFileName(source);

        if (!options.SeparateFiles || targets.Count < 2)
        {
            return [new ProfileExportFile(baseName + extension, BuildWorkbook(source, targets, options.UseXlsx))];
        }

        // 多文件模式下每个内容各存一个文件，文件名带内容后缀以便区分。
        var files = new List<ProfileExportFile>();
        foreach (var target in targets)
        {
            files.Add(new ProfileExportFile(
                $"{baseName}_{GetTargetName(target)}{extension}",
                BuildWorkbook(source, [target], options.UseXlsx)));
        }

        return files;
    }

    private static List<ProfileExportTarget> ToTargets(ProfileExcelExportOptions options)
    {
        var targets = new List<ProfileExportTarget>();
        if (options.ExportClassPlan)
        {
            targets.Add(ProfileExportTarget.ClassPlan);
        }

        if (options.ExportTimeLayout)
        {
            targets.Add(ProfileExportTarget.TimeLayout);
        }

        if (options.ExportSubject)
        {
            targets.Add(ProfileExportTarget.Subject);
        }

        return targets;
    }

    private static string GetTargetName(ProfileExportTarget target) => target switch
    {
        ProfileExportTarget.ClassPlan => "课表",
        ProfileExportTarget.TimeLayout => "时间表",
        _ => "科目"
    };

    private byte[] BuildWorkbook(
        ProfileExportSource source,
        IReadOnlyList<ProfileExportTarget> targets,
        bool useXlsx)
    {
        IWorkbook workbook = useXlsx ? new XSSFWorkbook() : new HSSFWorkbook();
        var styles = new WorkbookStyles(workbook);
        var subjects = ToCaseInsensitiveLookup(source.Profile.Subjects);
        var timeLayouts = ToCaseInsensitiveLookup(source.Profile.TimeLayouts);

        foreach (var target in targets)
        {
            switch (target)
            {
                case ProfileExportTarget.ClassPlan:
                    WriteClassPlanSheets(workbook, styles, source, subjects, timeLayouts);
                    break;
                case ProfileExportTarget.TimeLayout:
                    WriteTimeLayoutSheet(workbook, styles, source.Profile.TimeLayouts, subjects);
                    break;
                case ProfileExportTarget.Subject:
                    WriteSubjectSheet(workbook, styles, source.Profile.Subjects);
                    break;
            }
        }

        if (workbook.NumberOfSheets == 0)
        {
            var sheet = workbook.CreateSheet("空");
            WriteMessageRow(sheet, styles, "没有可导出的内容。");
        }

        using var buffer = new MemoryStream();
        workbook.Write(buffer, false);
        return buffer.ToArray();
    }

    /// <summary>生成建议的文件名（不含扩展名）。</summary>
    public static string BuildSuggestedFileName(ProfileExportSource source)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(source.ProfileName
            .Where(c => !invalid.Contains(c))
            .ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "ClassIsland课表";
        }

        return $"{name}_导出_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    #region 档案定位

    private string ResolveProfilesFolder()
    {
        foreach (var root in EnumerateAppRootCandidates())
        {
            var profiles = Path.Combine(root, ProfilesFolderName);
            if (Directory.Exists(profiles))
            {
                return profiles;
            }
        }

        throw new DirectoryNotFoundException(
            $"找不到 ClassIsland 档案目录（{ProfilesFolderName}）。请确认 ClassIsland 已运行过并且存在档案。");
    }

    private IEnumerable<string> EnumerateAppRootCandidates()
    {
        // 插件目录形如 <AppRoot>/Plugins/<PluginId>，据此反推应用根目录。
        var pluginFolder = GlobalConstants.Information.PluginFolder;
        if (!string.IsNullOrWhiteSpace(pluginFolder))
        {
            var derived = TryGetFullPath(Path.Combine(pluginFolder, "..", ".."));
            if (derived != null)
            {
                yield return derived;
            }
        }

        var fromHost = TryGetFullPath(CommonDirectories.AppRootFolderPath);
        if (fromHost != null)
        {
            yield return fromHost;
        }
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string ResolveCurrentProfileFile(string profilesFolder)
    {
        var candidates = new List<string>();

        if (IsManagementEnabled())
        {
            candidates.Add(Path.Combine(profilesFolder, ManagementProfileFileName));
        }

        var selected = TryReadSelectedProfile();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var fileName = Path.GetFileName(selected.Trim());
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                candidates.Add(Path.Combine(profilesFolder, fileName));
            }
        }

        candidates.Add(Path.Combine(profilesFolder, "Default.json"));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 兜底：取档案目录下任意一个有效档案（排除备份与集控档案）。
        var fallback = Directory.EnumerateFiles(profilesFolder, "*.json")
            .Where(x => !x.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(Path.GetFileName(x), ManagementProfileFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (fallback != null)
        {
            return fallback;
        }

        throw new FileNotFoundException($"档案目录 {profilesFolder} 中没有可读取的档案文件。");
    }

    private string? TryReadSelectedProfile()
    {
        foreach (var root in EnumerateAppRootCandidates())
        {
            var settingsPath = Path.Combine(root, SettingsFileName);
            if (!File.Exists(settingsPath))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settingsPath, Encoding.UTF8));
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("SelectedProfile", out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[SystemTools]读取 {Path} 失败，将改用默认档案。", settingsPath);
            }
        }

        return null;
    }

    private bool IsManagementEnabled()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClassIsland",
            "Management",
            "Settings.json");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("IsManagementEnabled", out var value) &&
                   value.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SystemTools]读取集控设置 {Path} 失败。", path);
            return false;
        }
    }

    private static Dictionary<string, TValue> ToCaseInsensitiveLookup<TValue>(Dictionary<string, TValue> source) =>
        new(source, StringComparer.OrdinalIgnoreCase);

    #endregion

    #region 课表

    private void WriteClassPlanSheets(
        IWorkbook workbook,
        WorkbookStyles styles,
        ProfileExportSource source,
        IReadOnlyDictionary<string, ClassIslandSubject> subjects,
        IReadOnlyDictionary<string, ClassIslandTimeLayout> timeLayouts)
    {
        // 临时层课表是运行时派生数据，不纳入导出；其余课表全部导出，避免静默丢失内容。
        var plans = source.Profile.ClassPlans.Values
            .Where(x => !x.IsOverlay)
            .ToList();

        if (plans.Count == 0)
        {
            var emptySheet = workbook.CreateSheet("课表");
            WriteMessageRow(emptySheet, styles, "当前档案没有课表。");
            return;
        }

        var blocks = BuildClassPlanBlocks(source.Profile, plans, subjects);

        // 每个课表群（或每张需要单独展示的课表）占一个区块，区块之间空一行。
        var sheet = workbook.CreateSheet("课表");
        var rowIndex = 0;
        var columnCount = 0;
        var showBlockTitle = blocks.Count > 1;
        foreach (var block in blocks)
        {
            if (rowIndex > 0)
            {
                rowIndex++; // 区块之间空一行
            }

            if (showBlockTitle)
            {
                rowIndex = WriteBlockTitleRow(sheet, styles, rowIndex, block.Title, block.ColumnCount);
            }

            rowIndex = WriteTable(sheet, styles, rowIndex, block.Header, block.Rows);
            columnCount = Math.Max(columnCount, block.ColumnCount);
        }

        ApplyStandardLayout(sheet, columnCount);
    }

    /// <summary>按课表群把课表组织成若干区块。</summary>
    private static List<ClassPlanBlock> BuildClassPlanBlocks(
        ClassIslandProfileDocument profile,
        IReadOnlyList<ClassIslandClassPlan> plans,
        IReadOnlyDictionary<string, ClassIslandSubject> subjects)
    {
        var blocks = new List<ClassPlanBlock>();
        var groups = ToCaseInsensitiveLookup(profile.ClassPlanGroups);

        // 课表群顺序：默认课表群 → 其它普通课表群 → 全局课表群。
        var orderedGroups = groups
            .OrderBy(x => GetGroupSortKey(x.Key, x.Value))
            .ThenBy(x => x.Value.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var weeklyBlocks = new List<ClassPlanBlock>();

        foreach (var group in orderedGroups)
        {
            var groupPlans = plans
                .Where(x => string.Equals(x.AssociatedGroup.ToString(), group.Key, StringComparison.OrdinalIgnoreCase))
                .Where(x => IsWeeklyClassPlan(x))
                .ToList();
            if (groupPlans.Count == 0)
            {
                continue;
            }

            foreach (var plan in groupPlans)
            {
                assigned.Add(plan.AssociatedGroup.ToString());
            }

            weeklyBlocks.Add(BuildWeeklyBlock(group.Value.Name, groupPlans, subjects));
        }

        // 未匹配到已知课表群的常规课表归入一个区块。
        var ungroupedWeekly = plans
            .Where(x => IsWeeklyClassPlan(x) && !assigned.Contains(x.AssociatedGroup.ToString()))
            .ToList();
        if (ungroupedWeekly.Count > 0)
        {
            weeklyBlocks.Add(BuildWeeklyBlock("未分类课表群", ungroupedWeekly, subjects));
        }

        blocks.AddRange(weeklyBlocks);

        // 轮换、指定日期、循环或未默认启用的课表各自成块，避免信息丢失。
        foreach (var plan in plans.Where(x => !IsWeeklyClassPlan(x)))
        {
            blocks.Add(BuildSinglePlanBlock(plan, subjects));
        }

        return blocks;
    }

    private static int GetGroupSortKey(string groupId, ClassIslandClassPlanGroup group)
    {
        if (group.IsGlobal)
        {
            return 2;
        }

        return string.Equals(groupId, DefaultClassPlanGroupId, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    /// <summary>是否为可直接并入周视图的常规课表（默认启用、按周触发且不轮换）。</summary>
    private static bool IsWeeklyClassPlan(ClassIslandClassPlan plan) =>
        plan.IsEnabled && plan.TimeRule.Type == 0 && plan.TimeRule.WeekCountDiv <= 0;

    private static ClassPlanBlock BuildWeeklyBlock(
        string groupName,
        IReadOnlyList<ClassIslandClassPlan> plans,
        IReadOnlyDictionary<string, ClassIslandSubject> subjects)
    {
        // 以星期一至星期日的顺序排列出现的星期。
        var usedWeekDays = plans
            .Select(x => NormalizeWeekDay(x.TimeRule.WeekDay))
            .Distinct()
            .OrderBy(x => x == 0 ? 7 : x)
            .ToList();

        // 节次行数取各课表节数的最大值。
        var rowCount = plans
            .Select(x => x.Classes.Count)
            .DefaultIfEmpty(0)
            .Max();

        var header = new List<string> { "节次" };
        header.AddRange(usedWeekDays.Select(x => WeekDayNames[x]));

        var rows = new List<string[]>();
        for (var row = 0; row < rowCount; row++)
        {
            var values = new List<string> { (row + 1).ToString() };
            values.AddRange(usedWeekDays.Select(weekDay => BuildWeekDayCell(plans, weekDay, row, subjects)));
            rows.Add([.. values]);
        }

        // 课表群内全是空课表时，至少给出一行说明，避免只剩表头。
        if (rows.Count == 0)
        {
            rows.Add(["1", "该课表群没有课程。"]);
        }

        return new ClassPlanBlock(
            string.IsNullOrWhiteSpace(groupName) ? "未命名课表群" : groupName,
            [.. header],
            rows);
    }

    private static ClassPlanBlock BuildSinglePlanBlock(
        ClassIslandClassPlan plan,
        IReadOnlyDictionary<string, ClassIslandSubject> subjects)
    {
        var rows = new List<string[]>();
        for (var i = 0; i < plan.Classes.Count; i++)
        {
            var subject = GetSubjectAt(subjects, plan, i);
            rows.Add([(i + 1).ToString(), subject?.Name ?? ""]);
        }

        if (rows.Count == 0)
        {
            rows.Add(["1", "该课表没有课程。"]);
        }

        return new ClassPlanBlock(BuildSinglePlanBlockTitle(plan), ["节次", "科目"], rows);
    }

    private static string BuildSinglePlanBlockTitle(ClassIslandClassPlan plan)
    {
        var name = string.IsNullOrWhiteSpace(plan.Name) ? "未命名课表" : plan.Name.Trim();
        var rule = plan.TimeRule.Type switch
        {
            1 => "按指定日期启用",
            2 => $"每 {plan.TimeRule.LoopCycleDays} 天循环启用",
            _ => plan.TimeRule.WeekCountDiv > 0
                ? $"每 {plan.TimeRule.WeekCountDivTotal} 周中的第 {plan.TimeRule.WeekCountDiv} 周"
                : "未默认启用"
        };

        return $"{name}（{rule}）";
    }

    private static string BuildWeekDayCell(
        IReadOnlyList<ClassIslandClassPlan> plans,
        int weekDay,
        int index,
        IReadOnlyDictionary<string, ClassIslandSubject> subjects)
    {
        var names = new List<string>();
        foreach (var plan in plans.Where(x => NormalizeWeekDay(x.TimeRule.WeekDay) == weekDay))
        {
            var subject = GetSubjectAt(subjects, plan, index);
            if (subject == null || string.IsNullOrWhiteSpace(subject.Name))
            {
                continue;
            }

            if (!names.Contains(subject.Name))
            {
                names.Add(subject.Name);
            }
        }

        return string.Join("/", names);
    }

    private static ClassIslandSubject? GetSubjectAt(
        IReadOnlyDictionary<string, ClassIslandSubject> subjects,
        ClassIslandClassPlan plan,
        int index)
    {
        if (index < 0 || index >= plan.Classes.Count)
        {
            return null;
        }

        var classInfo = plan.Classes[index];
        if (!classInfo.IsEnabled || classInfo.SubjectId == Guid.Empty)
        {
            return null;
        }

        return subjects.TryGetValue(classInfo.SubjectId.ToString(), out var subject) ? subject : null;
    }

    private static int NormalizeWeekDay(int weekDay) => weekDay is >= 0 and <= 6 ? weekDay : 0;

    #endregion

    #region 时间表

    private static void WriteTimeLayoutSheet(
        IWorkbook workbook,
        WorkbookStyles styles,
        IReadOnlyDictionary<string, ClassIslandTimeLayout> timeLayouts,
        IReadOnlyDictionary<string, ClassIslandSubject> subjects)
    {
        var sheet = workbook.CreateSheet("时间表");

        var layouts = timeLayouts.Values
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (layouts.Count == 0)
        {
            WriteMessageRow(sheet, styles, "当前档案没有时间表。");
            ApplyStandardLayout(sheet, 7);
            return;
        }

        // 每个时间表占一个区块，区块之间空一行。
        var header = new[] { "序号", "类型", "开始", "结束", "时长（分钟）", "默认科目 / 说明" };
        var showBlockTitle = layouts.Count > 1;
        var rowIndex = 0;
        foreach (var layout in layouts)
        {
            if (rowIndex > 0)
            {
                rowIndex++; // 区块之间空一行
            }

            if (showBlockTitle)
            {
                rowIndex = WriteBlockTitleRow(sheet, styles, rowIndex, GetTimeLayoutTitle(layout), header.Length);
            }

            var rows = new List<string[]>();
            var classIndex = 0;
            for (var i = 0; i < layout.Layouts.Count; i++)
            {
                var item = layout.Layouts[i];
                var note = "";
                if (item.TimeType == 0)
                {
                    classIndex++;
                    note = item.DefaultClassId != Guid.Empty &&
                           subjects.TryGetValue(item.DefaultClassId.ToString(), out var defaultSubject) &&
                           !string.IsNullOrWhiteSpace(defaultSubject.Name)
                        ? $"默认：{defaultSubject.Name}"
                        : $"第 {classIndex} 节课";
                }
                else if (item.TimeType == 1)
                {
                    note = string.IsNullOrWhiteSpace(item.BreakName) ? "课间休息" : item.BreakName;
                }

                rows.Add([
                    (i + 1).ToString(),
                    FormatTimeType(item.TimeType),
                    FormatTime(item.StartTime),
                    FormatTime(item.EndTime),
                    FormatDuration(item),
                    note
                ]);
            }

            if (rows.Count == 0)
            {
                rows.Add(["", "", "", "", "", "该时间表没有时间点。"]);
            }

            rowIndex = WriteTable(sheet, styles, rowIndex, header, rows);
        }

        ApplyStandardLayout(sheet, header.Length);
    }

    private static string GetTimeLayoutTitle(ClassIslandTimeLayout layout)
    {
        var name = string.IsNullOrWhiteSpace(layout.Name) ? "未命名时间表" : layout.Name.Trim();
        return layout.IsOverlay ? $"{name}（临时层）" : name;
    }

    #endregion

    #region 科目

    private static void WriteSubjectSheet(
        IWorkbook workbook,
        WorkbookStyles styles,
        IReadOnlyDictionary<string, ClassIslandSubject> subjects)
    {
        var sheet = workbook.CreateSheet("科目");
        WriteHeaderRow(sheet, styles, "科目", "简称", "教师", "户内外");

        var rowIndex = 1;
        foreach (var subject in subjects.Values
                     .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            WriteBodyRow(sheet, styles, rowIndex++,
                subject.Name,
                subject.Initial,
                subject.TeacherName,
                FormatOutDoor(subject.IsOutDoor));
        }

        if (rowIndex == 1)
        {
            WriteBodyRow(sheet, styles, 1, "当前档案没有科目。");
        }

        ApplyStandardLayout(sheet, 4);
    }

    #endregion

    #region 表格写入

    private static void WriteHeaderRow(ISheet sheet, WorkbookStyles styles, params string[] values)
    {
        var row = sheet.CreateRow(0);
        row.HeightInPoints = (float)StandardRowHeightPoints;
        for (var i = 0; i < values.Length; i++)
        {
            styles.ApplyHeader(row.CreateCell(i), values[i]);
        }
    }

    private static void WriteBodyRow(ISheet sheet, WorkbookStyles styles, int rowIndex, params string[] values)
    {
        var row = sheet.CreateRow(rowIndex);
        row.HeightInPoints = (float)StandardRowHeightPoints;
        for (var i = 0; i < values.Length; i++)
        {
            styles.ApplyBody(row.CreateCell(i), values[i]);
        }
    }

    private static void WriteMessageRow(ISheet sheet, WorkbookStyles styles, string message)
    {
        WriteBodyRow(sheet, styles, 0, message);
        sheet.SetColumnWidth(0, StandardColumnWidth);
    }

    /// <summary>
    /// 从指定行开始写入「表头 + 数据行」，返回下一个可用行号。
    /// 所有行使用标准样式（表头加粗灰底），并固定行高。
    /// </summary>
    private static int WriteTable(
        ISheet sheet,
        WorkbookStyles styles,
        int startRowIndex,
        IReadOnlyList<string> header,
        IReadOnlyList<string[]> rows)
    {
        var rowIndex = startRowIndex;

        var headerRow = sheet.CreateRow(rowIndex++);
        headerRow.HeightInPoints = (float)StandardRowHeightPoints;
        for (var i = 0; i < header.Count; i++)
        {
            styles.ApplyHeader(headerRow.CreateCell(i), header[i]);
        }

        foreach (var values in rows)
        {
            var row = sheet.CreateRow(rowIndex++);
            row.HeightInPoints = (float)StandardRowHeightPoints;
            for (var i = 0; i < values.Length; i++)
            {
                styles.ApplyBody(row.CreateCell(i), values[i]);
            }
        }

        return rowIndex;
    }

    /// <summary>写入区块标题行（跨列合并），返回下一个可用行号。</summary>
    private static int WriteBlockTitleRow(
        ISheet sheet,
        WorkbookStyles styles,
        int rowIndex,
        string title,
        int columnCount)
    {
        var row = sheet.CreateRow(rowIndex);
        row.HeightInPoints = (float)StandardRowHeightPoints;
        var cell = row.CreateCell(0);
        styles.ApplyTitle(cell, title);

        if (columnCount > 1)
        {
            sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(rowIndex, rowIndex, 0, columnCount - 1));
        }

        return rowIndex + 1;
    }

    /// <summary>套用标准模板的列宽与冻结窗格（冻结首行与首列）。</summary>
    private static void ApplyStandardLayout(ISheet sheet, int columnCount)
    {
        for (var i = 0; i < columnCount; i++)
        {
            sheet.SetColumnWidth(i, StandardColumnWidth);
        }

        sheet.CreateFreezePane(1, 1);
    }

    #endregion

    #region 格式化

    private static string FormatTime(TimeSpan time) =>
        time.Days > 0
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}"
            : $"{time.Hours:00}:{time.Minutes:00}";

    private static string FormatDuration(ClassIslandTimeLayoutItem item)
    {
        var duration = item.EndTime - item.StartTime;
        return duration.TotalMinutes <= 0 ? "" : $"{Math.Round(duration.TotalMinutes)}";
    }

    private static string FormatTimeType(int timeType) => timeType switch
    {
        0 => "上课",
        1 => "课间",
        2 => "分割线",
        3 => "行动",
        _ => "未知"
    };

    private static string FormatOutDoor(bool isOutDoor) => isOutDoor ? "户外" : "室内";

    #endregion

    /// <summary>
    /// 按标准模板创建并复用单元格样式：微软雅黑 14 磅、四边细边框、居中对齐并自动换行，
    /// 表头额外使用加粗与灰色填充。
    /// </summary>
    private sealed class WorkbookStyles
    {
        private readonly ICellStyle _header;
        private readonly ICellStyle _body;
        private readonly ICellStyle _title;

        public WorkbookStyles(IWorkbook workbook)
        {
            var headerFont = workbook.CreateFont();
            headerFont.FontName = "微软雅黑";
            headerFont.IsBold = true;
            headerFont.FontHeightInPoints = 14;
            _header = workbook.CreateCellStyle();
            _header.SetFont(headerFont);
            _header.Alignment = HorizontalAlignment.Center;
            _header.VerticalAlignment = VerticalAlignment.Center;
            _header.WrapText = true;
            _header.FillForegroundColor = IndexedColors.Grey25Percent.Index;
            _header.FillPattern = FillPattern.SolidForeground;
            ApplyBorder(_header);

            // 区块标题使用左对齐加粗，不加填充，读起来像小标题而非表头。
            var titleFont = workbook.CreateFont();
            titleFont.FontName = "微软雅黑";
            titleFont.IsBold = true;
            titleFont.FontHeightInPoints = 14;
            _title = workbook.CreateCellStyle();
            _title.SetFont(titleFont);
            _title.Alignment = HorizontalAlignment.Left;
            _title.VerticalAlignment = VerticalAlignment.Center;
            _title.WrapText = true;
            ApplyBorder(_title);

            var bodyFont = workbook.CreateFont();
            bodyFont.FontName = "微软雅黑";
            bodyFont.FontHeightInPoints = 14;
            _body = workbook.CreateCellStyle();
            _body.SetFont(bodyFont);
            _body.Alignment = HorizontalAlignment.Center;
            _body.VerticalAlignment = VerticalAlignment.Center;
            _body.WrapText = true;
            ApplyBorder(_body);
        }

        public void ApplyHeader(ICell cell, string text)
        {
            cell.SetCellValue(text);
            cell.CellStyle = _header;
        }

        public void ApplyTitle(ICell cell, string text)
        {
            cell.SetCellValue(text);
            cell.CellStyle = _title;
        }

        public void ApplyBody(ICell cell, string text)
        {
            cell.SetCellValue(text);
            cell.CellStyle = _body;
        }

        private static void ApplyBorder(ICellStyle style)
        {
            style.BorderTop = BorderStyle.Thin;
            style.BorderBottom = BorderStyle.Thin;
            style.BorderLeft = BorderStyle.Thin;
            style.BorderRight = BorderStyle.Thin;
        }
    }
}

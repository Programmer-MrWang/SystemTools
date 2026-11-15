using System.Globalization;
using System.Text.RegularExpressions;
using SystemTools.Contracts;

namespace SystemTools.Common.Layout;

public sealed record XrandrOutput(string Name, bool BuiltIn, bool Primary, int X, int Y, string? Mode, string? Rate, string Rotation, string[] Modes);
public static class XrandrLayout
{
    public static IReadOnlyList<XrandrOutput> Parse(string text)
    {
        var rows = new List<XrandrOutput>();
        string? name = null, mode = null, rate = null; var x = 0; var y = 0; var primary = false; var rotation = "normal";
        var modes = new List<string>();
        void Flush() { if (name is not null) rows.Add(new(name, Regex.IsMatch(name, "^(eDP|LVDS|DSI)", RegexOptions.IgnoreCase), primary, x, y, mode, rate, rotation, modes.ToArray())); }
        foreach (var line in text.Split('\n'))
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                Flush(); name = null; mode = rate = null; modes.Clear(); x = y = 0; rotation = "normal";
                var header = Regex.Match(line, @"^(?<name>[A-Za-z0-9_.:-]+) connected(?<primary> primary)?(?: (?<mode>\d+x\d+)(?<x>[+-]\d+)(?<y>[+-]\d+))?(?: (?<rotation>normal|left|right|inverted))?");
                if (!header.Success) continue;
                name = header.Groups["name"].Value; primary = header.Groups["primary"].Success;
                if (header.Groups["mode"].Success) { mode = header.Groups["mode"].Value; x = int.Parse(header.Groups["x"].Value, CultureInfo.InvariantCulture); y = int.Parse(header.Groups["y"].Value, CultureInfo.InvariantCulture); }
                if (header.Groups["rotation"].Success) rotation = header.Groups["rotation"].Value;
            }
            else if (name is not null)
            {
                var row = Regex.Match(line, @"^\s+(?<mode>\d+x\d+)\s+(?<rates>.+)$");
                if (!row.Success) continue;
                modes.Add(row.Groups["mode"].Value);
                foreach (var value in row.Groups["rates"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (value.Contains('*')) { mode = row.Groups["mode"].Value; rate = value.TrimEnd('*', '+'); }
            }
        }
        Flush(); return rows;
    }
    public static string[] RestoreArguments(IReadOnlyList<XrandrOutput> rows)
    {
        var arguments = new List<string>();
        foreach (var row in rows)
        {
            arguments.AddRange(["--output", row.Name]);
            if (row.Mode is null) arguments.Add("--off");
            else
            {
                arguments.AddRange(["--mode", row.Mode, "--pos", $"{row.X}x{row.Y}", "--rotate", row.Rotation]);
                if (row.Rate is { } rate) arguments.AddRange(["--rate", rate]);
                if (row.Primary) arguments.Add("--primary"); else arguments.Add("--noprimary");
            }
        }
        return arguments.ToArray();
    }
    public static string[] ApplyArguments(IReadOnlyList<XrandrOutput> rows, DisplayLayout layout)
    {
        if (rows.Count < 2 || rows.Any(x => x.Rotation != "normal")) throw new InvalidOperationException("需要至少两台显示器，并使用正常旋转方向。");
        var included = rows.Where(row => layout switch { DisplayLayout.InternalOnly => row.BuiltIn, DisplayLayout.ExternalOnly => !row.BuiltIn, _ => true }).ToArray();
        if (included.Length == 0) throw new InvalidOperationException("请求的内置或外部显示器不存在。");
        var primary = included.FirstOrDefault(x => x.Primary) ?? included[0];
        var common = layout == DisplayLayout.Mirror ? primary.Modes.FirstOrDefault(mode => included.All(x => x.Modes.Contains(mode))) : null;
        if (layout == DisplayLayout.Mirror && common is null) throw new InvalidOperationException("显示器没有共同模式，无法可靠镜像。");
        var arguments = new List<string>(); var right = 0;
        foreach (var row in rows.OrderByDescending(x => x.Name == primary.Name))
        {
            arguments.AddRange(["--output", row.Name]);
            if (!included.Contains(row)) { arguments.Add("--off"); continue; }
            var mode = common ?? row.Mode ?? row.Modes.FirstOrDefault() ?? throw new InvalidOperationException("显示器没有可用模式。");
            arguments.AddRange(["--mode", mode, "--pos", $"{(layout == DisplayLayout.Mirror ? 0 : right)}x0", "--rotate", "normal"]);
            if (row.Name == primary.Name) arguments.Add("--primary");
            right += int.Parse(mode.Split('x')[0], CultureInfo.InvariantCulture);
        }
        return arguments.ToArray();
    }
}

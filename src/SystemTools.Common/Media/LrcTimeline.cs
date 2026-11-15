using System.Globalization;
using System.Text.RegularExpressions;

namespace SystemTools.Common.Media;

public sealed class LrcTimeline
{
    private readonly (TimeSpan At, string Text)[] _lines;
    public LrcTimeline(string text)
    {
        if (text.Length > 1024 * 1024) throw new InvalidDataException("歌词文件过大。");
        var offsetMatch = Regex.Match(text, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        var offset = offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        var lines = new List<(TimeSpan At, string Text)>();
        foreach (var line in text.Split('\n'))
        {
            var matches = Regex.Matches(line, @"\[(\d{1,4}):(\d{2}(?:\.\d{1,3})?)\]");
            if (matches.Count == 0) continue;
            var content = line[(matches[^1].Index + matches[^1].Length)..].Trim();
            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                if (seconds >= 60) continue;
                lines.Add((TimeSpan.FromMilliseconds(Math.Max(0, (minutes * 60 + seconds) * 1000 + offset)), content));
            }
        }
        _lines = lines.OrderBy(line => line.At).ToArray();
    }
    public string At(TimeSpan position) => _lines.LastOrDefault(line => line.At <= position).Text ?? "";
}

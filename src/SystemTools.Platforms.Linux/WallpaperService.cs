using System.Text.Json;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class WallpaperService : IDesktopWallpaper
{
    private const string Schema = "org.gnome.desktop.background";
    private static readonly string[] Keys = ["picture-uri", "picture-uri-dark", "picture-options"];
    private static string? Find(string tool) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(path => Path.Combine(path, tool)).FirstOrDefault(File.Exists);
    private static string Desktop => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
    private static bool Gnome => Desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase) || Desktop.Contains("Unity", StringComparison.OrdinalIgnoreCase);
    private static bool Kde => Desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase);
    private static string Qdbus => Find("qdbus6") ?? Find("qdbus") ?? throw new NativeFeatureException(CapabilityState.MissingDependency, "请安装 KDE D-Bus 客户端工具。");
    private static async Task<string> Run(string tool, CancellationToken token, params string[] arguments)
    {
        var result = await CommandRunner.RunAsync(tool, arguments, token, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0) throw new NativeFeatureException(CapabilityState.Unsupported, "桌面环境拒绝壁纸操作，请检查会话与桌面服务。");
        return result.StandardOutput.Trim();
    }
    private static Task<string> Plasma(string script, CancellationToken token) => Run(Qdbus, token, "org.kde.plasmashell", "/PlasmaShell", "org.kde.PlasmaShell.evaluateScript", script);
    private static string Gsettings => Find("gsettings") ?? throw new NativeFeatureException(CapabilityState.MissingDependency, "请安装 GNOME 设置客户端。");
    public Capability Probe()
    {
        if (!OperatingSystem.IsLinux()) return new(CapabilityState.Unsupported);
        if (!Gnome && !Kde) return new(CapabilityState.Unsupported, "当前仅提供 GNOME 与 KDE Plasma 壁纸适配，原配置已保留。");
        try { ReadAsync(default).GetAwaiter().GetResult(); return new(CapabilityState.Available); }
        catch (Exception error) when (error is IOException or NativeFeatureException or TimeoutException)
        { return new(CapabilityState.MissingDependency, "无法连接桌面壁纸服务，请检查 GNOME/KDE 会话与客户端工具。"); }
    }
    public async Task<WallpaperState> ReadAsync(CancellationToken token)
    {
        if (Gnome)
        {
            var available = (await Run(Gsettings, token, "list-keys", Schema)).Split('\n').ToHashSet();
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in Keys.Where(available.Contains)) values[key] = await Run(Gsettings, token, "get", Schema, key);
            if (!values.ContainsKey(Keys[0]) || !values.ContainsKey(Keys[2])) throw new NativeFeatureException(CapabilityState.Unsupported, "GNOME 壁纸设置不完整。");
            var uri = Unquote(values[Keys[0]]);
            return new("linux:gnome", JsonSerializer.Serialize(values), Uri.TryCreate(uri, UriKind.Absolute, out var image) && image.IsFile ? image.LocalPath : null);
        }
        if (Kde)
        {
            var snapshot = await Plasma("var ds=desktops();var out=[];for(var i=0;i<ds.length;i++){var d=ds[i];d.currentConfigGroup=['Wallpaper',d.wallpaperPlugin,'General'];out.push({id:d.id,plugin:d.wallpaperPlugin,image:d.readConfig('Image',''),fill:d.readConfig('FillMode',2)});}JSON.stringify(out);", token);
            using var parsed = JsonDocument.Parse(snapshot);
            if (parsed.RootElement.GetArrayLength() == 0) throw new NativeFeatureException(CapabilityState.MissingDevice, "没有 KDE 桌面目标。");
            var first = parsed.RootElement[0].GetProperty("image").GetString();
            return new("linux:kde", snapshot, Uri.TryCreate(first, UriKind.Absolute, out var image) && image.IsFile ? image.LocalPath : null);
        }
        throw new NativeFeatureException(CapabilityState.Unsupported, "此桌面环境尚无壁纸实现。");
    }
    public async Task<WallpaperState> ApplyAsync(string imagePath, int fitStyle, CancellationToken token)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("壁纸图片不存在。", imagePath);
        if (fitStyle is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(fitStyle));
        var original = await ReadAsync(token);
        try
        {
            if (Gnome)
            {
                var values = JsonSerializer.Deserialize<SortedDictionary<string, string>>(original.Snapshot)!;
                foreach (var key in values.Keys.ToArray()) values[key] = key == Keys[2]
                    ? Quote(new[] { "wallpaper", "centered", "stretched", "zoom", "scaled", "spanned" }[fitStyle]) : Quote(new Uri(imagePath).AbsoluteUri);
                await WriteGnome(values, token);
            }
            else
            {
                if (fitStyle == 5) throw new NativeFeatureException(CapabilityState.Unsupported, "KDE 当前适配不提供跨屏拼接，请选择其他布局。");
                var fill = new[] { 3, 6, 0, 2, 1 }[fitStyle];
                var path = JsonSerializer.Serialize(new Uri(imagePath).AbsoluteUri);
                await Plasma($"var ds=desktops();for(var i=0;i<ds.length;i++){{var d=ds[i];d.wallpaperPlugin='org.kde.image';d.currentConfigGroup=['Wallpaper','org.kde.image','General'];d.writeConfig('Image',{path});d.writeConfig('FillMode',{fill});}}", token);
            }
            var result = await ReadAsync(CancellationToken.None);
            if (result.ImagePath != imagePath) throw new IOException("桌面没有确认新的壁纸图片。");
            return result;
        }
        catch { await WriteSnapshot(original, CancellationToken.None); throw; }
    }
    public async Task RestoreAsync(WallpaperState expected, WallpaperState original, CancellationToken token)
    {
        var current = await ReadAsync(token);
        if (current.Identity != expected.Identity || current.Snapshot != expected.Snapshot)
            throw new InvalidOperationException("壁纸已被用户或其他应用修改，已保留当前壁纸。");
        await WriteSnapshot(original, token);
    }
    private static async Task WriteSnapshot(WallpaperState state, CancellationToken token)
    {
        if (state.Identity == "linux:gnome") await WriteGnome(JsonSerializer.Deserialize<SortedDictionary<string, string>>(state.Snapshot)!, token);
        else
        {
            using var document = JsonDocument.Parse(state.Snapshot);
            var script = "var saved=" + state.Snapshot + ";var ds=desktops();if(ds.length!==saved.length)throw 'Desktop set changed';for(var i=0;i<saved.length;i++){var s=saved[i];var d=ds.filter(function(v){return v.id===s.id;})[0];if(!d)throw 'Desktop missing';d.wallpaperPlugin=s.plugin;d.currentConfigGroup=['Wallpaper',s.plugin,'General'];d.writeConfig('Image',s.image);d.writeConfig('FillMode',s.fill);}";
            await Plasma(script, token);
        }
    }
    private static async Task WriteGnome(SortedDictionary<string, string> values, CancellationToken token)
    { foreach (var item in values) await Run(Gsettings, token, "set", Schema, item.Key, item.Value); }
    private static string Quote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    private static string Unquote(string value) => value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' ? value[1..^1].Replace("\\'", "'").Replace("\\\\", "\\") : value;
}

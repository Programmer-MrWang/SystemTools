using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using ClassIsland;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Models.Plugin;
using SystemTools.Contracts;

var stage = Path.GetFullPath(args[0]);
var temporary = Path.GetFullPath(args[1]);
var checks = 0;
void Check(bool value, string description)
{
    if (!value) throw new Exception(description);
    Console.WriteLine("PASS " + ++checks + ": " + description);
}
void Reject(PlatformEnvironment environment)
{
    try { PlatformSelection.Select(environment); }
    catch (PlatformNotSupportedException) { Check(true, "拒绝 " + environment); return; }
    throw new Exception("Unsupported environment accepted: " + environment);
}
Check(PlatformSelection.Select(new("Windows", Architecture.X64)) == "win-x64", "Windows x64 选择规则");
Check(PlatformSelection.Select(new("MacOS", Architecture.Arm64)) == "osx-arm64", "Mac ARM64 选择规则");
Check(PlatformSelection.Select(new("Linux", Architecture.X64, "x11", ":0")) == "linux-x64", "原生 X11 选择规则");
Reject(new("MacOS", Architecture.X64));
Reject(new("Windows", Architecture.Arm64));
Reject(new("Linux", Architecture.X64, "wayland", ":0", "wayland-0"));
Reject(new("Linux", Architecture.X64, "x11", ":0", "wayland-0"));
Reject(new("Linux", Architecture.X64, "x11", null));
Reject(new("Linux", Architecture.Arm64, "x11", ":0"));
Reject(new("Android", Architecture.Arm64));

using (var stream = File.OpenRead(Path.Combine(stage, "SystemTools.dll")))
using (var pe = new PEReader(stream))
{
    var metadata = pe.GetMetadataReader();
    var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray();
    Check(!references.Any(n => n.StartsWith("SystemTools.Platforms.") || n is "System.Windows.Forms" or "System.Speech" or "System.Management" or "DlibDotNet" or "OpenCvSharp" or "SuperAutoIsland.Interface"), "入口元数据没有平台/可选依赖的静态引用");
}

var rid = PlatformSelection.Select(PlatformEnvironment.Current());
// This is the actual host source at the recorded commit. It delegates host types
// to Default because the package contains no duplicate host assemblies.
Assembly Load(string directory, out PluginLoadContext context)
{
    var info = new PluginInfo { Manifest = new PluginManifest { Id = "SystemTools.M1.Tests" } };
    context = new PluginLoadContext(info, Path.Combine(directory, "SystemTools.dll"), false);
    return context.LoadFromAssemblyName(new AssemblyName("SystemTools"));
}
object LoadBackend(Assembly entry, string directory)
{
    // Exercise the production explicit-path loader, not a mock implementation.
    var method = entry.GetType("SystemTools.BackendLoader", true)!.GetMethod("Load", BindingFlags.Static | BindingFlags.Public)!;
    return method.Invoke(null, [directory, rid])!;
}
var entry = Load(stage, out var context);
var exported = entry.ExportedTypes.ToArray();
Check(exported.Single(t => t.Name == "Plugin").BaseType == typeof(PluginBase), "宿主枚举入口公开类型且 PluginBase 身份一致");
foreach (var type in exported) _ = type.GetCustomAttributes().ToArray();
Check(true, "初始化前反射入口所有属性成功");
var backend = LoadBackend(entry, stage);
var descriptor = backend.GetType().GetMethod("Describe")!.Invoke(backend, null)!;
Check((string)descriptor.GetType().GetProperty("RuntimeId")!.GetValue(descriptor)! == rid, "宿主解析器加载本平台真实模块");
Check(context.Assemblies.Count(a => a.GetName().Name!.StartsWith("SystemTools.Platforms.")) == 1, "只加载一个平台模块");
Check(context.Assemblies.Count(a => a.GetName().Name == "SystemTools.Contracts") == 1, "契约只加载一次");
Check(!context.Assemblies.Any(a => a.GetName().Name!.StartsWith("ClassIsland") || a.GetName().Name!.StartsWith("Avalonia")), "宿主程序集未被插件重复载入");

var foreignDirectory = Path.Combine(temporary, "foreign-corrupt");
var missingDirectory = Path.Combine(temporary, "missing-backend");
Directory.CreateDirectory(foreignDirectory);
Directory.CreateDirectory(missingDirectory);
var local = PlatformSelection.ModuleName(rid) + ".dll";
foreach (var file in Directory.EnumerateFiles(stage))
{
    var name = Path.GetFileName(file);
    File.Copy(file, Path.Combine(foreignDirectory, name));
    if (name != local) File.Copy(file, Path.Combine(missingDirectory, name));
}
foreach (var other in new[] { "win-x64", "linux-x64", "osx-arm64" })
{
    var name = PlatformSelection.ModuleName(other) + ".dll";
    if (name != local) File.WriteAllText(Path.Combine(foreignDirectory, name), "corrupt foreign platform sentinel");
}
var isolated = Load(foreignDirectory, out var second);
LoadBackend(isolated, foreignDirectory);
Check(second.Assemblies.Count(a => a.GetName().Name!.StartsWith("SystemTools.Platforms.")) == 1, "损坏的异平台 DLL 不影响当前平台加载");
var missing = Load(missingDirectory, out _);
try { LoadBackend(missing, missingDirectory); throw new Exception("Missing backend accepted"); }
catch (TargetInvocationException ex) when (ex.InnerException is FileNotFoundException)
{ Check(true, "本平台 DLL 缺失时明确失败"); }
Console.WriteLine($"PASS: {checks} M1 boundary checks; native runtime under test: {rid}");

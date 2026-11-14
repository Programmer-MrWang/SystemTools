using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

internal static class NativeBridge
{
    private const string Library = "SystemToolsNative";
    static NativeBridge()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeBridge).Assembly, (name, assembly, _) =>
        {
            if (name != Library) return 0;
            var root = Path.GetDirectoryName(assembly.Location)!;
            foreach (var path in new[] { Path.Combine(root, "platforms/osx-arm64/libSystemToolsNative.dylib"), Path.Combine(root, "libSystemToolsNative.dylib") })
                if (File.Exists(path)) return NativeLibrary.Load(path);
            throw new DllNotFoundException("请重新安装包含 Mac 原生模块的完整插件包。");
        });
    }

    internal static JsonElement Call(object request)
    {
        var pointer = Invoke(JsonSerializer.Serialize(request));
        if (pointer == 0) throw new IOException("原生模块未返回结果。");
        try
        {
            using var document = JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer)!);
            return document.RootElement.Clone();
        }
        finally { Free(pointer); }
    }
    internal static Capability Probe(string operation)
    {
        try { return Status(Call(new { operation })); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return new(CapabilityState.MissingDependency, "Mac 原生模块缺失或版本不符，请重新安装完整插件。"); }
    }
    internal static Capability Status(JsonElement result) => new(
        Enum.TryParse<CapabilityState>(result.GetProperty("state").GetString(), out var state) ? state : CapabilityState.Unsupported,
        result.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : "");
    internal static JsonElement Require(object request)
    {
        var result = Call(request);
        var status = Status(result);
        if (!status.CanExecute) throw new NativeFeatureException(status.State, status.Reason);
        return result;
    }
    [DllImport(Library, EntryPoint = "systemtools_native_call")]
    private static extern nint Invoke([MarshalAs(UnmanagedType.LPUTF8Str)] string request);
    [DllImport(Library, EntryPoint = "systemtools_native_free")]
    private static extern void Free(nint memory);
}

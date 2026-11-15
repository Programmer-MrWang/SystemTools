using System.Diagnostics;
using System.Security.Cryptography;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class SecretServiceProtector : IStoredCredentialProtector
{
    public string Scheme => "secret-service:v1:";
    private static string? Tool => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.Combine(x, "secret-tool")).FirstOrDefault(File.Exists);
    public Capability Availability => !OperatingSystem.IsLinux() ? new(CapabilityState.Unsupported, "需要 Linux Secret Service。")
        : Tool is null || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
            ? new(CapabilityState.MissingDependency, "请安装 libsecret 的 secret-tool，并在已登录的 Secret Service 会话中使用；密钥不回退保存为明文。")
            : new(CapabilityState.Available, "Secret Service 工具与会话已就绪，保存/读取仍需已解锁的密钥环授权。");
    public string Protect(string plaintext)
    {
        if (plaintext.Length > 32768 || plaintext.Contains('\0')) throw new ArgumentException("凭据格式或长度不支持。");
        var id = Guid.NewGuid().ToString("N");
        if (!Run("store", id, plaintext).Success) throw new CryptographicException("Secret Service 未能保存密钥；请解锁密钥环并重试，原配置保持不变。");
        return Scheme + id;
    }
    public bool TryUnprotect(string reference, out string plaintext)
    {
        plaintext = "";
        if (!TryId(reference, out var id) || !Availability.CanExecute) return false;
        var result = Run("lookup", id, null);
        if (!result.Success) return false;
        plaintext = result.Value.EndsWith('\n') ? result.Value[..^1] : result.Value;
        return true;
    }
    public bool Delete(string reference) => TryId(reference, out var id) && Run("clear", id, null).Success;
    private bool TryId(string reference, out string id)
    {
        id = reference.StartsWith(Scheme, StringComparison.Ordinal) ? reference[Scheme.Length..] : "";
        return id.Length == 32 && Guid.TryParseExact(id, "N", out _);
    }
    private (bool Success, string Value) Run(string operation, string id, string? secret)
    {
        if (!Availability.CanExecute) return (false, "");
        try
        {
            var info = new ProcessStartInfo(Tool!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new System.Text.UTF8Encoding(false),
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            info.ArgumentList.Add(operation);
            if (operation == "store") info.ArgumentList.Add("--label=SystemTools API credential");
            foreach (var argument in new[] { "application", "io.github.programmer-mrwang.systemtools", "credential-id", id }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (secret is not null) process.StandardInput.Write(secret);
            process.StandardInput.Close();
            if (!process.WaitForExit(15000)) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); return (false, ""); }
            Task.WhenAll(output, error).Wait(TimeSpan.FromSeconds(2));
            return process.ExitCode == 0 && output.IsCompletedSuccessfully ? (true, output.Result) : (false, "");
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or AggregateException) { return (false, ""); }
    }
}

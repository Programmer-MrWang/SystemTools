using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public sealed class CommandTextSpeech(string runtime) : ITextSpeech
{
    private string? Executable => runtime == "osx-arm64" && File.Exists("/usr/bin/say")
        ? "/usr/bin/say" : runtime == "linux-x64" ? MediaFiles.FindExecutable("espeak-ng") : null;
    public Capability Probe() => Executable is not null ? new(CapabilityState.Available) :
        new(CapabilityState.MissingDependency, "系统朗读服务不可用；Linux 请安装 espeak-ng。");
    public async Task SpeakAsync(string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return;
        var capability = Probe();
        if (!capability.CanExecute) throw new NativeFeatureException(capability.State, capability.Reason);
        var result = await CommandRunner.RunAsync(Executable!, ["--", text], token, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException("系统朗读未完成，请检查声音输出和所选语音。");
    }
}

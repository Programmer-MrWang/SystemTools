using SystemTools.Contracts;

namespace SystemTools;

public sealed partial class NativeBackgroundAppearance
{
    private partial Task ProcessTextAsync(BackgroundFrame frame, CancellationToken token) => Task.CompletedTask;
}

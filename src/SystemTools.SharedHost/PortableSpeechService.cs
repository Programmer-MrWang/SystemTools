using Microsoft.Extensions.Hosting;
using SystemTools.Common.Media;
using SystemTools.Contracts;

namespace SystemTools;

public sealed class PortableSpeechService(PlatformMediaServices media) : IHostedService
{
    public SpeechInputRouter Input { get; } = new(media.Speech, media.Stopping);
    public Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public Task StopAsync(CancellationToken token) => Input.DisposeAsync().AsTask();
}

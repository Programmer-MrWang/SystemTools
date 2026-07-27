using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SystemTools.Services;

public sealed record AiChatMessage(string Role, string Content);

public sealed record AiChatCompletionResult(string Id, string Model, string Content);

public interface IOpenAiCompatibleService
{
    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<AiChatCompletionResult> CompleteChatAsync(
        IReadOnlyList<AiChatMessage> messages,
        string? model = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamChatCompletionAsync(
        IReadOnlyList<AiChatMessage> messages,
        string? model = null,
        CancellationToken cancellationToken = default);
}

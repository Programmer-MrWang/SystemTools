using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SystemTools.Services;

public sealed record AiToolCall(string Id, string Name, string Arguments);

public sealed record AiToolDefinition(
    string Name,
    string Description,
    JsonElement Parameters);

public abstract record AiChatContent;

public sealed record AiTextContent(string Text) : AiChatContent;

public sealed record AiDataContent(
    ReadOnlyMemory<byte> Data,
    string MediaType,
    string? FileName = null) : AiChatContent;

public sealed record AiChatMessage(string Role, string? Content)
{
    public IReadOnlyList<AiChatContent>? Contents { get; init; }

    public string? ToolCallId { get; init; }

    public IReadOnlyList<AiToolCall>? ToolCalls { get; init; }
}

public sealed record AiChatCompletionResult(
    string Id,
    string Model,
    string Content,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    string? FinishReason = null);

public sealed record AiChatStreamUpdate(
    string ContentDelta,
    AiChatCompletionResult? Completion = null);

public interface IOpenAiCompatibleService
{
    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<AiChatCompletionResult> CompleteChatAsync(
        IReadOnlyList<AiChatMessage> messages,
        string? model = null,
        CancellationToken cancellationToken = default);

    Task<AiChatCompletionResult> CompleteChatWithToolsAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        string? model = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamChatCompletionAsync(
        IReadOnlyList<AiChatMessage> messages,
        string? model = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AiChatStreamUpdate> StreamChatCompletionWithToolsAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        string? model = null,
        CancellationToken cancellationToken = default);
}

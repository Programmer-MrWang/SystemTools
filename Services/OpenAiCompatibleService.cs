using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.ConfigHandlers;

namespace SystemTools.Services;

public sealed class OpenAiCompatibleService : IOpenAiCompatibleService, IDisposable
{
    private const string NativeAttachmentCapabilityHint =
        " 若请求包含图片或 PDF，请检查所选模型和兼容端点是否支持相应多模态内容。";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly MainConfigHandler _configHandler;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(100)
    };

    public OpenAiCompatibleService(MainConfigHandler configHandler)
    {
        _configHandler = configHandler;
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();

        using var request = CreateRequest(HttpMethod.Get, "models");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseBody);

        ModelsResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<ModelsResponse>(responseBody, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("模型接口返回的内容不是有效的 OpenAI JSON 格式。", ex);
        }

        if (result?.Data is null)
        {
            throw new InvalidDataException("模型接口响应中缺少 data 列表。");
        }

        return result.Data
            .Select(x => x.Id?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AiChatCompletionResult> CompleteChatAsync(
        IReadOnlyList<AiChatMessage> messages,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        return await CompleteChatCoreAsync(messages, null, model, cancellationToken);
    }

    public async Task<AiChatCompletionResult> CompleteChatWithToolsAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        if (tools is null || tools.Count == 0)
        {
            throw new ArgumentException("至少需要提供一个 AI 工具。", nameof(tools));
        }

        return await CompleteChatCoreAsync(messages, tools, model, cancellationToken);
    }

    private async Task<AiChatCompletionResult> CompleteChatCoreAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools,
        string? model,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var (selectedModel, payload) = CreateChatCompletionPayload(messages, model, stream: false, tools);

        using var request = CreateRequest(HttpMethod.Post, "chat/completions");
        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseBody, ContainsNativeAttachments(messages));

        return ParseChatCompletionResponse(responseBody, selectedModel);
    }

    private static AiChatCompletionResult ParseChatCompletionResponse(
        string responseBody,
        string selectedModel)
    {
        var normalizedResponseBody = NormalizeJsonResponseBody(responseBody);
        ChatCompletionResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<ChatCompletionResponse>(normalizedResponseBody, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("AI 接口返回的内容不是有效的 OpenAI JSON 格式。", ex);
        }

        var choice = result?.Choices?.FirstOrDefault();
        var message = choice?.Message;
        if (message?.ToolCalls?.Any(x => !string.Equals(x.Type, "function", StringComparison.Ordinal) ||
                                        string.IsNullOrWhiteSpace(x.Id) ||
                                        string.IsNullOrWhiteSpace(x.Function?.Name)) == true)
        {
            throw new InvalidDataException("AI 接口返回了无效的函数工具调用，已拒绝执行整个调用计划。");
        }

        if (message?.ToolCalls?
                .GroupBy(x => x.Id, StringComparer.Ordinal)
                .Any(group => group.Count() > 1) == true)
        {
            throw new InvalidDataException("AI 接口返回了重复的工具调用 id，已拒绝执行整个调用计划。");
        }

        if (message?.ToolCalls?.Length > 0 &&
            !string.Equals(choice?.FinishReason, "tool_calls", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "AI 接口未以 tool_calls 终态完成工具调用，已拒绝执行整个调用计划。");
        }

        var toolCalls = message?.ToolCalls?
            .Select(x => new AiToolCall(
                x.Id!,
                x.Function!.Name!,
                ValidateToolArguments(x.Function.Arguments)))
            .ToArray() ?? [];

        var messageContent = GetResponseText(message?.Content);
        if (string.IsNullOrWhiteSpace(messageContent) && !string.IsNullOrWhiteSpace(message?.Refusal))
        {
            messageContent = $"AI 拒绝了该请求：{message.Refusal}";
        }

        if (messageContent is null && toolCalls.Length == 0)
        {
            throw new InvalidDataException("AI 接口响应中既没有回复内容，也没有工具调用。");
        }

        return new AiChatCompletionResult(
            result?.Id ?? string.Empty,
            result?.Model ?? selectedModel,
            messageContent ?? string.Empty,
            toolCalls,
            choice?.FinishReason);
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(
        IReadOnlyList<AiChatMessage> messages,
        string? model = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in StreamChatCompletionCoreAsync(
                           messages,
                           tools: null,
                           model,
                           cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.ContentDelta))
            {
                yield return update.ContentDelta;
            }
        }
    }

    public async IAsyncEnumerable<AiChatStreamUpdate> StreamChatCompletionWithToolsAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        string? model = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (tools is null || tools.Count == 0)
        {
            throw new ArgumentException("至少需要提供一个 AI 工具。", nameof(tools));
        }

        await foreach (var update in StreamChatCompletionCoreAsync(
                           messages,
                           tools,
                           model,
                           cancellationToken))
        {
            yield return update;
        }
    }

    private async IAsyncEnumerable<AiChatStreamUpdate> StreamChatCompletionCoreAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition>? tools,
        string? model,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var hasNativeAttachments = ContainsNativeAttachments(messages);
        var (selectedModel, payload) = CreateChatCompletionPayload(messages, model, stream: true, tools);

        using var request = CreateRequest(HttpMethod.Post, "chat/completions");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson", 0.8));
        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, errorBody, hasNativeAttachments);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream);
        var receivedDataEvent = false;
        var receivedDoneEvent = false;
        var responseId = string.Empty;
        var responseModel = selectedModel;
        var content = new StringBuilder();
        var nonSseResponse = new StringBuilder();
        var toolCallBuilders = new Dictionary<int, StreamingToolCallBuilder>();
        string? finishReason = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var eventLine = line.TrimStart();
            if (!eventLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (!receivedDataEvent)
                {
                    nonSseResponse.AppendLine(line);
                }

                continue;
            }

            var eventData = eventLine["data:".Length..].Trim();
            if (eventData.Length == 0)
            {
                continue;
            }

            receivedDataEvent = true;
            if (string.Equals(eventData, "[DONE]", StringComparison.Ordinal))
            {
                receivedDoneEvent = true;
                break;
            }

            if (TryGetStreamErrorMessage(eventData) is { Length: > 0 } streamError)
            {
                var capabilityHint = hasNativeAttachments
                    ? NativeAttachmentCapabilityHint
                    : string.Empty;
                throw new InvalidOperationException($"AI 服务返回错误：{streamError}{capabilityHint}");
            }

            ChatCompletionChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(eventData, SerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("AI 流式接口返回了无效的 JSON 数据。", ex);
            }

            if (!string.IsNullOrWhiteSpace(chunk?.Id))
            {
                responseId = chunk.Id;
            }

            if (!string.IsNullOrWhiteSpace(chunk?.Model))
            {
                responseModel = chunk.Model;
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(choice?.FinishReason))
            {
                finishReason = choice.FinishReason;
            }

            var contentDelta = choice?.Delta?.Content;
            if (!string.IsNullOrEmpty(contentDelta))
            {
                content.Append(contentDelta);
                yield return new AiChatStreamUpdate(contentDelta);
            }

            var refusalDelta = choice?.Delta?.Refusal;
            if (!string.IsNullOrEmpty(refusalDelta))
            {
                content.Append(refusalDelta);
                yield return new AiChatStreamUpdate(refusalDelta);
            }

            foreach (var toolCallDelta in choice?.Delta?.ToolCalls ?? [])
            {
                AccumulateToolCallDelta(toolCallBuilders, toolCallDelta);
            }
        }

        if (!receivedDataEvent)
        {
            var responseBody = nonSseResponse.ToString().Trim();
            if (TryGetStreamErrorMessage(responseBody) is { Length: > 0 } responseError)
            {
                var capabilityHint = hasNativeAttachments
                    ? NativeAttachmentCapabilityHint
                    : string.Empty;
                throw new InvalidOperationException($"AI 服务返回错误：{responseError}{capabilityHint}");
            }

            AiChatCompletionResult completion;
            try
            {
                completion = ParseChatCompletionResponse(responseBody, selectedModel);
            }
            catch (InvalidDataException)
            {
                if (!TryParseJsonLinesChatCompletion(
                        responseBody,
                        selectedModel,
                        hasNativeAttachments,
                        out completion))
                {
                    throw;
                }
            }

            if (!string.IsNullOrEmpty(completion.Content))
            {
                yield return new AiChatStreamUpdate(completion.Content);
            }

            yield return new AiChatStreamUpdate(string.Empty, completion);
            yield break;
        }

        if (!receivedDoneEvent && string.IsNullOrWhiteSpace(finishReason))
        {
            throw new InvalidDataException("AI 流式响应在终止事件或 finish_reason 前中断。");
        }

        yield return new AiChatStreamUpdate(
            string.Empty,
            CreateStreamingCompletion(
                responseId,
                responseModel,
                content,
                toolCallBuilders,
                finishReason));
    }

    private static bool TryParseJsonLinesChatCompletion(
        string responseBody,
        string selectedModel,
        bool hasNativeAttachments,
        out AiChatCompletionResult completion)
    {
        completion = null!;
        var responseId = string.Empty;
        var responseModel = selectedModel;
        var content = new StringBuilder();
        var toolCallBuilders = new Dictionary<int, StreamingToolCallBuilder>();
        string? finishReason = null;
        var receivedChunk = false;
        var receivedDoneEvent = false;

        var lines = NormalizeJsonResponseBody(responseBody)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (string.Equals(line, "[DONE]", StringComparison.Ordinal))
            {
                receivedDoneEvent = true;
                continue;
            }

            if (TryGetStreamErrorMessage(line) is { Length: > 0 } streamError)
            {
                var capabilityHint = hasNativeAttachments
                    ? NativeAttachmentCapabilityHint
                    : string.Empty;
                throw new InvalidOperationException($"AI 服务返回错误：{streamError}{capabilityHint}");
            }

            ChatCompletionChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                return false;
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            if (choice is null ||
                (choice.Delta is null &&
                 (!receivedChunk || string.IsNullOrWhiteSpace(choice.FinishReason))))
            {
                return false;
            }

            receivedChunk = true;
            if (!string.IsNullOrWhiteSpace(chunk?.Id))
            {
                responseId = chunk.Id;
            }

            if (!string.IsNullOrWhiteSpace(chunk?.Model))
            {
                responseModel = chunk.Model;
            }

            if (!string.IsNullOrWhiteSpace(choice.FinishReason))
            {
                finishReason = choice.FinishReason;
            }

            if (!string.IsNullOrEmpty(choice.Delta?.Content))
            {
                content.Append(choice.Delta.Content);
            }

            if (!string.IsNullOrEmpty(choice.Delta?.Refusal))
            {
                content.Append(choice.Delta.Refusal);
            }

            foreach (var toolCallDelta in choice.Delta?.ToolCalls ?? [])
            {
                AccumulateToolCallDelta(toolCallBuilders, toolCallDelta);
            }
        }

        if (!receivedChunk)
        {
            return false;
        }

        if (!receivedDoneEvent && string.IsNullOrWhiteSpace(finishReason))
        {
            throw new InvalidDataException("AI JSON Lines 响应在终止事件或 finish_reason 前中断。");
        }

        completion = CreateStreamingCompletion(
            responseId,
            responseModel,
            content,
            toolCallBuilders,
            finishReason);
        return true;
    }

    private static AiChatCompletionResult CreateStreamingCompletion(
        string responseId,
        string responseModel,
        StringBuilder content,
        IDictionary<int, StreamingToolCallBuilder> toolCallBuilders,
        string? finishReason)
    {
        var toolCalls = toolCallBuilders
            .OrderBy(x => x.Key)
            .Select(x => x.Value.Build())
            .ToArray();
        if (toolCalls
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("AI 流式接口返回了重复的工具调用 id，已拒绝执行整个调用计划。");
        }

        if (toolCalls.Length > 0 &&
            !string.Equals(finishReason, "tool_calls", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "AI 流式接口未以 tool_calls 终态完成工具调用，已拒绝执行整个调用计划。");
        }

        if (content.Length == 0 && toolCalls.Length == 0)
        {
            throw new InvalidDataException("AI 流式响应中既没有回复内容，也没有工具调用。");
        }

        return new AiChatCompletionResult(
            responseId,
            responseModel,
            content.ToString(),
            toolCalls,
            finishReason);
    }

    private static void AccumulateToolCallDelta(
        IDictionary<int, StreamingToolCallBuilder> builders,
        ToolCallDelta toolCallDelta)
    {
        if (toolCallDelta.Index is null or < 0)
        {
            throw new InvalidDataException("AI 流式接口返回了缺少有效 index 的工具调用分片。");
        }

        if (!builders.TryGetValue(toolCallDelta.Index.Value, out var builder))
        {
            builder = new StreamingToolCallBuilder();
            builders.Add(toolCallDelta.Index.Value, builder);
        }

        builder.Add(toolCallDelta);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private void EnsureEnabled()
    {
        if (!_configHandler.Data.EnableAiService)
        {
            throw new InvalidOperationException("AI 服务尚未启用。");
        }
    }

    private (string Model, ChatCompletionRequest Payload) CreateChatCompletionPayload(
        IReadOnlyList<AiChatMessage> messages,
        string? model,
        bool stream,
        IReadOnlyList<AiToolDefinition>? tools)
    {
        if (messages is null || messages.Count == 0)
        {
            throw new ArgumentException("至少需要提供一条消息。", nameof(messages));
        }

        var selectedModel = string.IsNullOrWhiteSpace(model)
            ? _configHandler.Data.AiModel.Trim()
            : model.Trim();
        if (string.IsNullOrWhiteSpace(selectedModel))
        {
            throw new InvalidOperationException("尚未选择 AI 模型。");
        }

        ValidateMessageContents(messages);

        var payload = new ChatCompletionRequest
        {
            Model = selectedModel,
            Stream = stream,
            Messages = messages.Select(x => new ChatMessage
            {
                Role = x.Role,
                Content = CreateMessageContent(x),
                ToolCallId = x.ToolCallId,
                ToolCalls = x.ToolCalls?.Select(toolCall => new ToolCallPayload
                {
                    Id = toolCall.Id,
                    Function = new ToolFunctionPayload
                    {
                        Name = toolCall.Name,
                        Arguments = toolCall.Arguments
                    }
                }).ToArray()
            }).ToArray(),
            Tools = tools?.Select(tool => new ToolDefinitionPayload
            {
                Function = new ToolFunctionDefinitionPayload
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = tool.Parameters
                }
            }).ToArray()
        };

        return (selectedModel, payload);
    }

    private static object? CreateMessageContent(AiChatMessage message)
    {
        if (message.Contents is null || message.Contents.Count == 0)
        {
            return message.Content;
        }

        var parts = new List<object>();
        if (!string.IsNullOrEmpty(message.Content))
        {
            parts.Add(new TextContentPart { Text = message.Content });
        }

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case AiTextContent textContent:
                    parts.Add(new TextContentPart { Text = textContent.Text });
                    break;
                case AiDataContent { MediaType: var mediaType } dataContent
                    when mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                    parts.Add(new ImageContentPart
                    {
                        ImageUrl = new ImageUrlPayload
                        {
                            Url = BuildDataUrl(dataContent)
                        }
                    });
                    break;
                case AiDataContent dataContent
                    when string.Equals(
                        dataContent.MediaType,
                        "application/pdf",
                        StringComparison.OrdinalIgnoreCase):
                    parts.Add(new FileContentPart
                    {
                        File = new FilePayload
                        {
                            FileName = string.IsNullOrWhiteSpace(dataContent.FileName)
                                ? "attachment.pdf"
                                : dataContent.FileName,
                            FileData = BuildDataUrl(dataContent)
                        }
                    });
                    break;
                case AiDataContent dataContent:
                    throw new NotSupportedException($"不支持发送媒体类型 {dataContent.MediaType}。");
                default:
                    throw new NotSupportedException("不支持的 AI 消息内容类型。");
            }
        }

        return parts;
    }

    private static void ValidateMessageContents(IEnumerable<AiChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Contents is null || message.Contents.Count == 0)
            {
                continue;
            }

            if (!string.Equals(message.Role, "user", StringComparison.Ordinal))
            {
                throw new InvalidDataException("只有 user 消息可以携带图片、PDF 或附件文本内容。");
            }

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case AiTextContent { Text.Length: > 0 }:
                        break;
                    case AiTextContent:
                        throw new InvalidDataException("附件文本内容不能为空。");
                    case AiDataContent { Data.Length: 0 }:
                        throw new InvalidDataException("图片或 PDF 附件缺少原始数据。");
                    case AiDataContent dataContent
                        when IsSupportedImageMediaType(dataContent.MediaType):
                        break;
                    case AiDataContent dataContent
                        when string.Equals(
                            dataContent.MediaType,
                            "application/pdf",
                            StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(dataContent.FileName):
                        break;
                    case AiDataContent dataContent
                        when string.Equals(
                            dataContent.MediaType,
                            "application/pdf",
                            StringComparison.OrdinalIgnoreCase):
                        throw new InvalidDataException("PDF 附件必须包含文件名。");
                    case AiDataContent dataContent:
                        throw new InvalidDataException($"不支持发送媒体类型 {dataContent.MediaType}。");
                    default:
                        throw new InvalidDataException("不支持的 AI 消息内容类型。");
                }
            }
        }
    }

    private static bool IsSupportedImageMediaType(string mediaType)
    {
        return mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("image/gif", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateToolArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            throw new InvalidDataException("AI 工具调用缺少 function.arguments，已拒绝执行。");
        }

        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("AI 工具调用参数必须是完整的 JSON 对象，已拒绝执行。");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("AI 工具调用参数不是完整的 JSON 对象，已拒绝执行。", ex);
        }

        return arguments;
    }

    private static string BuildDataUrl(AiDataContent content)
    {
        return $"data:{content.MediaType};base64,{Convert.ToBase64String(content.Data.Span)}";
    }

    private static bool ContainsNativeAttachments(IEnumerable<AiChatMessage> messages)
    {
        return messages.Any(message => message.Contents?.Any(content => content is AiDataContent) == true);
    }

    private static string NormalizeJsonResponseBody(string responseBody)
    {
        var normalized = responseBody.Trim().TrimStart('\uFEFF');
        var firstLineEnd = normalized.IndexOfAny(['\r', '\n']);
        if (firstLineEnd < 0)
        {
            return normalized;
        }

        var openingFence = normalized[..firstLineEnd].Trim();
        if (!string.Equals(openingFence, "```", StringComparison.Ordinal) &&
            !string.Equals(openingFence, "```json", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var closingFence = normalized.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence <= firstLineEnd ||
            normalized[(closingFence + "```".Length)..].Trim().Length > 0)
        {
            return normalized;
        }

        return normalized[(firstLineEnd + 1)..closingFence].Trim();
    }

    private static string? GetResponseText(object? content)
    {
        return content switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => throw new InvalidDataException("AI 接口返回了非文本的回复内容。")
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, BuildEndpoint(relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var apiKey = _configHandler.Data.AiApiKey.Trim();
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    private Uri BuildEndpoint(string relativePath)
    {
        var configuredUrl = _configHandler.Data.AiApiUrl.Trim();
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("API 请求地址必须是有效的 HTTP 或 HTTPS 绝对地址。");
        }

        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("API 请求地址不能包含查询参数或片段。");
        }

        var baseUrl = configuredUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath);
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string responseBody,
        bool hasNativeAttachments = false)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = TryGetErrorMessage(responseBody);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = string.IsNullOrWhiteSpace(responseBody)
                ? response.ReasonPhrase
                : responseBody.Trim();
        }

        var capabilityHint = hasNativeAttachments
            ? NativeAttachmentCapabilityHint
            : string.Empty;

        throw new HttpRequestException(
            $"AI 服务请求失败（{(int)response.StatusCode} {response.StatusCode}）：{message}{capabilityHint}",
            null,
            response.StatusCode);
    }

    private static string? TryGetErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }

            if (error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error bodies are handled by the caller.
        }

        return null;
    }

    private static string? TryGetStreamErrorMessage(string eventData)
    {
        try
        {
            using var document = JsonDocument.Parse(eventData);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }

            if (error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            return error.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ModelsResponse
    {
        [JsonPropertyName("data")]
        public ModelInfo[]? Data { get; init; }
    }

    private sealed class ModelInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required ChatMessage[] Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolDefinitionPayload[]? Tools { get; init; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public object? Content { get; init; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; init; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ToolCallPayload[]? ToolCalls { get; init; }

        [JsonPropertyName("refusal")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Refusal { get; init; }
    }

    private sealed class TextContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "text";

        [JsonPropertyName("text")]
        public required string Text { get; init; }
    }

    private sealed class ImageContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "image_url";

        [JsonPropertyName("image_url")]
        public required ImageUrlPayload ImageUrl { get; init; }
    }

    private sealed class ImageUrlPayload
    {
        [JsonPropertyName("url")]
        public required string Url { get; init; }
    }

    private sealed class FileContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "file";

        [JsonPropertyName("file")]
        public required FilePayload File { get; init; }
    }

    private sealed class FilePayload
    {
        [JsonPropertyName("filename")]
        public required string FileName { get; init; }

        [JsonPropertyName("file_data")]
        public required string FileData { get; init; }
    }

    private sealed class ToolDefinitionPayload
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "function";

        [JsonPropertyName("function")]
        public required ToolFunctionDefinitionPayload Function { get; init; }
    }

    private sealed class ToolFunctionDefinitionPayload
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("parameters")]
        public JsonElement Parameters { get; init; }
    }

    private sealed class ToolCallPayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("type")]
        public string Type { get; init; } = "function";

        [JsonPropertyName("function")]
        public ToolFunctionPayload? Function { get; init; }
    }

    private sealed class ToolFunctionPayload
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; init; }
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("choices")]
        public ChatChoice[]? Choices { get; init; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class ChatCompletionChunk
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("choices")]
        public ChatChunkChoice[]? Choices { get; init; }

    }

    private sealed class ChatChunkChoice
    {
        [JsonPropertyName("delta")]
        public ChatDelta? Delta { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class ChatDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("refusal")]
        public string? Refusal { get; init; }

        [JsonPropertyName("tool_calls")]
        public ToolCallDelta[]? ToolCalls { get; init; }
    }

    private sealed class ToolCallDelta
    {
        [JsonPropertyName("index")]
        public int? Index { get; init; }

        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("function")]
        public ToolFunctionPayload? Function { get; init; }
    }

    private sealed class StreamingToolCallBuilder
    {
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _arguments = new();
        private string? _id;
        private string? _type;

        public void Add(ToolCallDelta delta)
        {
            if (!string.IsNullOrEmpty(delta.Id))
            {
                if (_id is not null && !string.Equals(_id, delta.Id, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("AI 流式接口为同一工具调用返回了相互冲突的 id。");
                }

                _id = delta.Id;
            }

            if (!string.IsNullOrEmpty(delta.Type))
            {
                if (_type is not null && !string.Equals(_type, delta.Type, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("AI 流式接口为同一工具调用返回了相互冲突的类型。");
                }

                _type = delta.Type;
            }

            if (!string.IsNullOrEmpty(delta.Function?.Name))
            {
                _name.Append(delta.Function.Name);
            }

            if (!string.IsNullOrEmpty(delta.Function?.Arguments))
            {
                _arguments.Append(delta.Function.Arguments);
            }
        }

        public AiToolCall Build()
        {
            if (!string.IsNullOrEmpty(_type) &&
                !string.Equals(_type, "function", StringComparison.Ordinal))
            {
                throw new InvalidDataException("AI 流式接口返回了非 function 类型的工具调用。");
            }

            if (string.IsNullOrWhiteSpace(_id) || _name.Length == 0)
            {
                throw new InvalidDataException("AI 流式接口返回了缺少 id 或函数名的工具调用。");
            }

            return new AiToolCall(
                _id,
                _name.ToString(),
                ValidateToolArguments(_arguments.Length == 0 ? null : _arguments.ToString()));
        }
    }

}

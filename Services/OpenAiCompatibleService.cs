using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.ConfigHandlers;

namespace SystemTools.Services;

public sealed class OpenAiCompatibleService : IOpenAiCompatibleService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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
        EnsureEnabled();
        var (selectedModel, payload) = CreateChatCompletionPayload(messages, model, stream: false);

        using var request = CreateRequest(HttpMethod.Post, "chat/completions");
        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseBody);

        ChatCompletionResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("AI 接口返回的内容不是有效的 OpenAI JSON 格式。", ex);
        }

        var content = result?.Choices?.FirstOrDefault()?.Message?.Content;
        if (content is null)
        {
            throw new InvalidDataException("AI 接口响应中缺少 choices[0].message.content。");
        }

        return new AiChatCompletionResult(
            result?.Id ?? string.Empty,
            result?.Model ?? selectedModel,
            content);
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(
        IReadOnlyList<AiChatMessage> messages,
        string? model = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var (_, payload) = CreateChatCompletionPayload(messages, model, stream: true);

        using var request = CreateRequest(HttpMethod.Post, "chat/completions");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = JsonContent.Create(payload, options: SerializerOptions);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, errorBody);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream);
        var receivedDataEvent = false;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eventData = line["data:".Length..].Trim();
            if (eventData.Length == 0)
            {
                continue;
            }

            receivedDataEvent = true;
            if (string.Equals(eventData, "[DONE]", StringComparison.Ordinal))
            {
                yield break;
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

            if (!string.IsNullOrWhiteSpace(chunk?.Error?.Message))
            {
                throw new InvalidOperationException($"AI 服务返回错误：{chunk.Error.Message}");
            }

            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(content))
            {
                yield return content;
            }
        }

        if (!receivedDataEvent)
        {
            throw new InvalidDataException("AI 服务未返回 OpenAI 兼容的 SSE 流式响应。");
        }
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
        bool stream)
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

        var payload = new ChatCompletionRequest
        {
            Model = selectedModel,
            Stream = stream,
            Messages = messages.Select(x => new ChatMessage
            {
                Role = x.Role,
                Content = x.Content
            }).ToArray()
        };

        return (selectedModel, payload);
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

    private static void EnsureSuccess(HttpResponseMessage response, string responseBody)
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

        throw new HttpRequestException(
            $"AI 服务请求失败（{(int)response.StatusCode} {response.StatusCode}）：{message}",
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
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
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
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
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
    }

    private sealed class ChatCompletionChunk
    {
        [JsonPropertyName("choices")]
        public ChatChunkChoice[]? Choices { get; init; }

        [JsonPropertyName("error")]
        public ApiError? Error { get; init; }
    }

    private sealed class ChatChunkChoice
    {
        [JsonPropertyName("delta")]
        public ChatDelta? Delta { get; init; }
    }

    private sealed class ChatDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}

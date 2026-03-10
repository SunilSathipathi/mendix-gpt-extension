// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// OpenAI Chat Completions integration
// ============================================================================
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.Models;
using Mendix.StudioPro.ExtensionsAPI;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Services;

[SupportedOSPlatform("windows")]
public class OpenAIService
{
    private readonly IHttpClientService _httpClientService;
    private readonly ConfigurationService _configService;
    private readonly ILogService _logService;
    private CancellationTokenSource? _currentCts;

    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAIService(
        IHttpClientService httpClientService,
        ConfigurationService configService,
        ILogService logService)
    {
        _httpClientService = httpClientService;
        _configService = configService;
        _logService = logService;
    }

    public async Task<ApiResponse> SendRequestAsync(
        SystemPromptParts systemPrompt,
        List<object> messages,
        List<Dictionary<string, object>>? tools,
        Action<string> onTextDelta,
        Action<string, string> onToolStart,
        Action<int, int, int>? onRetryWait = null,
        CancellationToken externalToken = default)
    {
        var apiKey = _configService.GetApiKey("openai");
        if (string.IsNullOrEmpty(apiKey))
            return ApiResponse.Error("No API key configured. Please set your OpenAI API key in Settings.");

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var ct = _currentCts.Token;

        try
        {
            var config = _configService.GetConfig();
            var requestJson = BuildRequestJson(systemPrompt, messages, tools, config);
            _logService.Info($"AIDE Lite: [OpenAI] Model: gpt-4o-mini, MaxTokens: {config.MaxTokens}, body: {requestJson.Length} chars");

            return await SendWithRetries(apiKey, requestJson, config, onTextDelta, onToolStart, onRetryWait, ct);
        }
        catch (OperationCanceledException)
        {
            return ApiResponse.Error("Request cancelled.", "cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logService.Error($"AIDE Lite: OpenAI network error: {ex.Message}");
            return ApiResponse.Error("Network error. Please check your connection.", "network");
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: OpenAI unexpected error: {ex.Message}");
            return ApiResponse.Error("An unexpected error occurred. Check the AIDE Lite log for details.");
        }
        finally
        {
            var cts = Interlocked.Exchange(ref _currentCts, null);
            cts?.Dispose();
        }
    }

    public void Cancel()
    {
        try { _currentCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static string BuildRequestJson(
        SystemPromptParts systemPrompt,
        List<object> messages,
        List<Dictionary<string, object>>? tools,
        AideLiteConfig config)
    {
        var openAiMessages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = $"{systemPrompt.StaticInstructions}\n\nAPP MODEL\n{systemPrompt.AppContext}"
            }
        };

        foreach (var msg in messages)
        {
            foreach (var converted in ConvertMessage(msg))
                openAiMessages.Add(converted);
        }

        var body = new JsonObject
        {
            ["model"] = "gpt-4o-mini",
            ["max_tokens"] = config.MaxTokens,
            ["messages"] = openAiMessages
        };

        var openAiTools = ConvertTools(tools);
        if (openAiTools != null)
            body["tools"] = openAiTools;

        return body.ToJsonString(JsonOptions);
    }

    private static JsonArray? ConvertTools(List<Dictionary<string, object>>? tools)
    {
        if (tools is not { Count: > 0 })
            return null;

        var result = new JsonArray();
        foreach (var tool in tools)
        {
            if (!tool.TryGetValue("name", out var nameObj) || nameObj is not string name)
                continue;

            tool.TryGetValue("description", out var descriptionObj);
            tool.TryGetValue("input_schema", out var schemaObj);
            JsonNode parameters;
            try
            {
                parameters = JsonSerializer.SerializeToNode(schemaObj, JsonOptions) ?? new JsonObject();
            }
            catch
            {
                parameters = new JsonObject();
            }

            result.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = name,
                    ["description"] = descriptionObj?.ToString() ?? "",
                    ["parameters"] = parameters
                }
            });
        }

        return result.Count > 0 ? result : null;
    }

    private static List<JsonObject> ConvertMessage(object message)
    {
        JsonNode? node;
        try
        {
            node = JsonSerializer.SerializeToNode(message, JsonOptions);
        }
        catch
        {
            return new List<JsonObject>();
        }

        if (node is not JsonObject msg)
            return new List<JsonObject>();

        var role = msg["role"]?.GetValue<string>() ?? "user";
        var content = msg["content"];
        if (content is JsonValue value)
        {
            return new List<JsonObject>
            {
                new()
                {
                    ["role"] = role,
                    ["content"] = value.GetValue<string>()
                }
            };
        }

        if (content is not JsonArray blocks)
            return new List<JsonObject>();

        if (role == "assistant")
            return ConvertAssistantMessage(blocks);

        if (blocks.Any(block => block?["type"]?.GetValue<string>() == "tool_result"))
            return ConvertToolResultMessages(blocks);

        return new List<JsonObject>
        {
            new()
            {
                ["role"] = role,
                ["content"] = ConvertContentParts(blocks)
            }
        };
    }

    private static List<JsonObject> ConvertAssistantMessage(JsonArray blocks)
    {
        var text = new StringBuilder();
        var toolCalls = new JsonArray();

        foreach (var block in blocks)
        {
            var type = block?["type"]?.GetValue<string>();
            if (type == "text")
            {
                text.Append(block?["text"]?.GetValue<string>() ?? "");
                continue;
            }

            if (type != "tool_use")
                continue;

            toolCalls.Add(new JsonObject
            {
                ["id"] = block?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = block?["name"]?.GetValue<string>() ?? "",
                    ["arguments"] = (block?["input"] ?? new JsonObject()).ToJsonString()
                }
            });
        }

        var message = new JsonObject
        {
            ["role"] = "assistant"
        };

        if (text.Length > 0)
            message["content"] = text.ToString();

        if (toolCalls.Count > 0)
            message["tool_calls"] = toolCalls;

        return new List<JsonObject> { message };
    }

    private static List<JsonObject> ConvertToolResultMessages(JsonArray blocks)
    {
        var result = new List<JsonObject>();
        foreach (var block in blocks)
        {
            if (block?["type"]?.GetValue<string>() != "tool_result")
                continue;

            var content = block["content"]?.GetValue<string>() ?? "";
            var isError = block["is_error"]?.GetValue<bool>() ?? false;
            result.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = block["tool_use_id"]?.GetValue<string>() ?? "",
                ["content"] = isError ? $"ERROR: {content}" : content
            });
        }
        return result;
    }

    private static JsonArray ConvertContentParts(JsonArray blocks)
    {
        var parts = new JsonArray();
        foreach (var block in blocks)
        {
            var type = block?["type"]?.GetValue<string>();
            switch (type)
            {
                case "text":
                    parts.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = block?["text"]?.GetValue<string>() ?? ""
                    });
                    break;
                case "image":
                    var mediaType = block?["source"]?["media_type"]?.GetValue<string>() ?? "image/png";
                    var base64 = block?["source"]?["data"]?.GetValue<string>() ?? "";
                    parts.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = $"data:{mediaType};base64,{base64}"
                        }
                    });
                    break;
            }
        }
        return parts;
    }

    private async Task<ApiResponse> SendWithRetries(
        string apiKey,
        string requestJson,
        AideLiteConfig config,
        Action<string> onTextDelta,
        Action<string, string> onToolStart,
        Action<int, int, int>? onRetryWait,
        CancellationToken ct)
    {
        var maxRetries = config.RetryMaxAttempts;
        var retryDelay = config.RetryDelaySeconds;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var httpClient = _httpClientService.CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, ct);
            var statusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return await ParseResponseAsync(response, onTextDelta, onToolStart, ct);

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var truncatedBody = errorBody.Length > 500 ? errorBody[..500] + "...(truncated)" : errorBody;
            _logService.Error($"AIDE Lite: [OpenAI] Error body: {truncatedBody}");

            if (statusCode == 401)
                return ApiResponse.Error("Invalid API key. Please check your settings.", "auth_error");

            if (!IsRetryableStatus(statusCode) || attempt >= maxRetries)
                return statusCode == 429
                    ? ApiResponse.Error($"Rate limit exceeded after {maxRetries} retries. Please try again later.", "rate_limit")
                    : ApiResponse.Error($"API error ({statusCode}). Check the AIDE Lite log for details.");

            var delaySec = GetRetryDelay(response, retryDelay, attempt);
            onRetryWait?.Invoke(attempt + 1, delaySec, maxRetries);
            await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
        }

        return ApiResponse.Error("Unexpected retry state.");
    }

    private static bool IsRetryableStatus(int code) => code is 429 or 500 or 502 or 503;

    private static int GetRetryDelay(HttpResponseMessage response, int configuredDelay, int attempt)
    {
        if (response.Headers.TryGetValues("retry-after", out var values))
        {
            var retryAfter = values.FirstOrDefault();
            if (int.TryParse(retryAfter, out var headerSeconds) && headerSeconds > 0)
                return Math.Min(headerSeconds, 300);
        }

        var exponentialDelay = configuredDelay * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * 0.3 * exponentialDelay;
        return (int)Math.Min(exponentialDelay + jitter, 300);
    }

    private static async Task<ApiResponse> ParseResponseAsync(
        HttpResponseMessage response,
        Action<string> onTextDelta,
        Action<string, string> onToolStart,
        CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonNode.Parse(body);
        if (json == null)
            return ApiResponse.Error("OpenAI returned an empty response.");

        var choice = json["choices"]?[0];
        var message = choice?["message"];
        if (message == null)
            return ApiResponse.Error("OpenAI response did not include a message.");

        var fullText = ExtractText(message["content"]);
        if (!string.IsNullOrEmpty(fullText))
            onTextDelta(fullText);

        var toolCalls = new List<ToolCall>();
        if (message["tool_calls"] is JsonArray toolCallArray)
        {
            foreach (var toolCall in toolCallArray)
            {
                var id = toolCall?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                var name = toolCall?["function"]?["name"]?.GetValue<string>() ?? "";
                var arguments = toolCall?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                toolCalls.Add(new ToolCall
                {
                    Id = id,
                    Name = name,
                    InputJson = string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments
                });
                onToolStart(name, id);
            }
        }

        var usage = json["usage"];
        return new ApiResponse
        {
            IsSuccess = true,
            FullText = fullText,
            StopReason = choice?["finish_reason"]?.GetValue<string>() ?? "",
            ToolCalls = toolCalls,
            InputTokens = usage?["prompt_tokens"]?.GetValue<int>() ?? 0,
            OutputTokens = usage?["completion_tokens"]?.GetValue<int>() ?? 0
        };
    }

    private static string ExtractText(JsonNode? contentNode)
    {
        if (contentNode == null)
            return string.Empty;

        if (contentNode is JsonValue value)
            return value.GetValue<string>();

        if (contentNode is not JsonArray parts)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var text = part?["text"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
                sb.Append(text);
        }
        return sb.ToString();
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TodoAgent.Api.Agent.Llm;

/// <summary>
/// The second adapter: the OpenAI chat completions dialect, which Ollama, vLLM,
/// LM Studio and most hosted providers also speak. Same neutral types in, same
/// neutral types out; only this file knows that tool arguments travel as a JSON
/// string and that each tool result is its own message.
/// </summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    public const string ProviderName = "openai";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiLlmClient> _logger;

    public OpenAiLlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<OpenAiLlmClient> logger)
    {
        _http = http;
        _options = options.Value.OpenAi;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct)
    {
        var body = new ChatRequest
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            Messages = ToWire(messages),
            Tools = tools.Count == 0 ? null : tools.Select(t => new ToolSpec(new FunctionSpec(t.Name, t.Description, t.InputSchema))).ToList(),
        };

        var endpoint = new Uri(_options.BaseUrl.TrimEnd('/') + "/chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        _logger.LogDebug("OpenAI-compatible request to {Endpoint}: {Model}, {MessageCount} messages", endpoint, body.Model, body.Messages.Count);

        HttpResponseMessage response;
        string responseText;
        try
        {
            response = await _http.SendAsync(request, ct);
            responseText = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException($"Could not reach {endpoint}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmException($"The request to {endpoint} timed out.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmException($"{endpoint.Host} returned {(int)response.StatusCode}: {responseText}");
            }
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ChatResponse>(responseText, Json);
            var choice = parsed?.Choices.FirstOrDefault()
                ?? throw new LlmException("The response contained no choices.");
            return FromWire(choice);
        }
        catch (JsonException ex)
        {
            throw new LlmException($"The response could not be parsed: {ex.Message}", ex);
        }
    }

    // ---- neutral -> wire -------------------------------------------------

    private static List<JsonElement> ToWire(IReadOnlyList<LlmMessage> messages)
    {
        var wire = new List<JsonElement>();

        foreach (var message in messages)
        {
            switch (message)
            {
                case SystemMessage system:
                    wire.Add(Element(new { role = "system", content = system.Text }));
                    break;

                case UserMessage user:
                    wire.Add(Element(new { role = "user", content = user.Text }));
                    break;

                case AssistantMessage { Payload: { Provider: ProviderName } payload }:
                    // The model's own message, replayed exactly as it came back.
                    wire.Add(payload.Content);
                    break;

                case AssistantMessage assistant:
                    wire.Add(Element(new
                    {
                        role = "assistant",
                        content = assistant.Text,
                        tool_calls = assistant.ToolCalls.Count == 0 ? null : assistant.ToolCalls.Select(call => new
                        {
                            id = call.Id,
                            type = "function",
                            function = new { name = call.Name, arguments = call.Input.GetRawText() },
                        }).ToList(),
                    }));
                    break;

                case ToolResultMessage result:
                    // No is_error flag in this dialect; the model reads the prefix instead.
                    wire.Add(Element(new
                    {
                        role = "tool",
                        tool_call_id = result.ToolCallId,
                        content = result.IsError ? $"Error: {result.Content}" : result.Content,
                    }));
                    break;
            }
        }

        return wire;
    }

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Json);

    // ---- wire -> neutral -------------------------------------------------

    private static LlmResponse FromWire(Choice choice)
    {
        var message = choice.Message;
        var toolCalls = (message.ToolCalls ?? [])
            .Select(call => new ToolCall(call.Id, call.Function.Name, ParseArguments(call.Function.Arguments)))
            .ToList();

        var stopReason = choice.FinishReason switch
        {
            "stop" => StopReason.EndTurn,
            "tool_calls" or "function_call" => StopReason.ToolUse,
            "length" => StopReason.MaxTokens,
            "content_filter" => StopReason.Refusal,
            _ => toolCalls.Count > 0 ? StopReason.ToolUse : StopReason.Other,
        };

        var payload = new ProviderPayload(ProviderName, message.Raw);
        var text = string.IsNullOrEmpty(message.Content) ? null : message.Content;

        return new LlmResponse(text, toolCalls, stopReason, payload);
    }

    private static JsonElement ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        try
        {
            return JsonDocument.Parse(arguments).RootElement.Clone();
        }
        catch (JsonException)
        {
            // Hand the model back what it sent, so its next turn can see the problem.
            return JsonSerializer.SerializeToElement(new { malformed_arguments = arguments });
        }
    }

    // ---- wire types ------------------------------------------------------

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("max_tokens")] public int? MaxTokens { get; init; }
        [JsonPropertyName("messages")] public required List<JsonElement> Messages { get; init; }
        [JsonPropertyName("tools")] public List<ToolSpec>? Tools { get; init; }
    }

    private sealed record ToolSpec([property: JsonPropertyName("function")] FunctionSpec Function)
    {
        [JsonPropertyName("type")] public string Type => "function";
    }

    private sealed record FunctionSpec(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice> Choices { get; init; } = [];
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public required AssistantWire Message { get; init; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
    }

    /// <summary>
    /// The assistant message as received. <see cref="Raw"/> keeps the whole object
    /// (including fields this code does not model, such as reasoning content some
    /// servers add) so it can be replayed unchanged.
    /// </summary>
    [JsonConverter(typeof(AssistantWireConverter))]
    private sealed class AssistantWire
    {
        public string? Content { get; init; }
        public List<ToolCallWire>? ToolCalls { get; init; }
        public JsonElement Raw { get; init; }
    }

    private sealed class ToolCallWire
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("function")] public required FunctionCallWire Function { get; init; }
    }

    private sealed class FunctionCallWire
    {
        [JsonPropertyName("name")] public required string Name { get; init; }
        [JsonPropertyName("arguments")] public string? Arguments { get; init; }
    }

    private sealed class AssistantWireConverter : JsonConverter<AssistantWire>
    {
        public override AssistantWire Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = JsonElement.ParseValue(ref reader);
            return new AssistantWire
            {
                Raw = raw,
                Content = raw.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null,
                ToolCalls = raw.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array
                    ? calls.Deserialize<List<ToolCallWire>>(options)
                    : null,
            };
        }

        public override void Write(Utf8JsonWriter writer, AssistantWire value, JsonSerializerOptions options) =>
            value.Raw.WriteTo(writer);
    }
}

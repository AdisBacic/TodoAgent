using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TodoAgent.Api.Agent.Llm;

/// <summary>
/// Talks to the Anthropic Messages API with a plain HttpClient. Everything about
/// the wire format lives in this file: the request shape, the content-block model,
/// and how neutral messages map onto it.
/// </summary>
public sealed class AnthropicLlmClient : ILlmClient
{
    public const string ProviderName = "anthropic";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicLlmClient> _logger;

    public AnthropicLlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<AnthropicLlmClient> logger)
    {
        _http = http;
        _options = options.Value.Anthropic;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct)
    {
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new LlmException("No Anthropic API key. Set Llm:Anthropic:ApiKey (dotnet user-secrets) or the ANTHROPIC_API_KEY environment variable.");

        var body = new MessagesRequest
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = string.Join("\n\n", messages.OfType<SystemMessage>().Select(m => m.Text)) is { Length: > 0 } system ? system : null,
            Tools = tools.Select(t => new ToolSpec(t.Name, t.Description, t.InputSchema)).ToList(),
            Messages = ToWire(messages),
            OutputConfig = _options.Effort is null ? null : new OutputConfig(_options.Effort),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        _logger.LogDebug("Anthropic request: {Model}, {MessageCount} messages, {ToolCount} tools", body.Model, body.Messages.Count, body.Tools.Count);

        // Every way the call can fail surfaces as one exception type, so the
        // loop has one thing to catch and one message to show.
        HttpResponseMessage response;
        string responseText;
        try
        {
            response = await _http.SendAsync(request, ct);
            responseText = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException($"Could not reach Anthropic at {_http.BaseAddress}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmException("The Anthropic request timed out.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmException($"Anthropic returned {(int)response.StatusCode}: {responseText}");
            }
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MessagesResponse>(responseText, Json)
                ?? throw new LlmException("Anthropic returned an empty body.");
            return FromWire(parsed);
        }
        catch (JsonException ex)
        {
            throw new LlmException($"Anthropic returned a body that could not be parsed: {ex.Message}", ex);
        }
    }

    // ---- neutral -> wire -------------------------------------------------

    private static List<WireMessage> ToWire(IReadOnlyList<LlmMessage> messages)
    {
        var wire = new List<WireMessage>();

        for (var i = 0; i < messages.Count; i++)
        {
            switch (messages[i])
            {
                case SystemMessage:
                    break; // sent as the top-level "system" field

                case UserMessage user:
                    wire.Add(new WireMessage("user", [new ContentBlock { Type = "text", Text = user.Text }]));
                    break;

                case AssistantMessage assistant:
                    var blocks = AssistantBlocks(assistant);
                    if (blocks.Count > 0)
                    {
                        wire.Add(new WireMessage("assistant", blocks));
                    }
                    break;

                case ToolResultMessage:
                    // Anthropic wants every result for one assistant turn in a
                    // single user message, so consecutive results are grouped.
                    var results = new List<ContentBlock>();
                    while (i < messages.Count && messages[i] is ToolResultMessage result)
                    {
                        results.Add(new ContentBlock
                        {
                            Type = "tool_result",
                            ToolUseId = result.ToolCallId,
                            Content = result.Content,
                            IsError = result.IsError ? true : null,
                        });
                        i++;
                    }
                    i--;
                    wire.Add(new WireMessage("user", results));
                    break;
            }
        }

        return wire;
    }

    private static List<ContentBlock> AssistantBlocks(AssistantMessage assistant)
    {
        // The model's own turn is replayed exactly as it came back, reasoning
        // blocks and signatures included. Only turns that did not come from this
        // provider (a fake in tests, a different provider) are rebuilt.
        if (assistant.Payload is { Provider: ProviderName } payload)
        {
            return payload.Content.Deserialize<List<ContentBlock>>(Json) ?? [];
        }

        var blocks = new List<ContentBlock>();
        if (!string.IsNullOrEmpty(assistant.Text))
        {
            blocks.Add(new ContentBlock { Type = "text", Text = assistant.Text });
        }

        foreach (var call in assistant.ToolCalls)
        {
            blocks.Add(new ContentBlock { Type = "tool_use", Id = call.Id, Name = call.Name, Input = call.Input });
        }

        return blocks;
    }

    // ---- wire -> neutral -------------------------------------------------

    private static LlmResponse FromWire(MessagesResponse response)
    {
        var text = string.Concat(response.Content.Where(b => b.Type == "text").Select(b => b.Text));
        var toolCalls = response.Content
            .Where(b => b.Type == "tool_use")
            .Select(b => new ToolCall(b.Id!, b.Name!, b.Input ?? EmptyObject()))
            .ToList();

        var stopReason = response.StopReason switch
        {
            "end_turn" or "stop_sequence" => StopReason.EndTurn,
            "tool_use" => StopReason.ToolUse,
            "max_tokens" => StopReason.MaxTokens,
            "refusal" => StopReason.Refusal,
            _ => StopReason.Other,
        };

        var payload = new ProviderPayload(ProviderName, JsonSerializer.SerializeToElement(response.Content, Json));

        return new LlmResponse(text.Length == 0 ? null : text, toolCalls, stopReason, payload);
    }

    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();

    // ---- wire types ------------------------------------------------------
    // Property names are spelled out so the request that goes over the wire
    // can be read straight off this file.

    private sealed class MessagesRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("max_tokens")] public required int MaxTokens { get; init; }
        [JsonPropertyName("system")] public string? System { get; init; }
        [JsonPropertyName("tools")] public required List<ToolSpec> Tools { get; init; }
        [JsonPropertyName("messages")] public required List<WireMessage> Messages { get; init; }
        [JsonPropertyName("output_config")] public OutputConfig? OutputConfig { get; init; }
    }

    private sealed record ToolSpec(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("input_schema")] JsonElement InputSchema);

    private sealed record OutputConfig([property: JsonPropertyName("effort")] string Effort);

    private sealed record WireMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] List<ContentBlock> Content);

    /// <summary>
    /// One shape for every block kind: text, tool_use, tool_result, thinking.
    /// Unknown fields are kept in <see cref="Extra"/> so reasoning blocks and
    /// their signatures survive a round trip untouched.
    /// </summary>
    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public required string Type { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("input")] public JsonElement? Input { get; init; }
        [JsonPropertyName("tool_use_id")] public string? ToolUseId { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("is_error")] public bool? IsError { get; init; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    private sealed class MessagesResponse
    {
        [JsonPropertyName("content")] public required List<ContentBlock> Content { get; init; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; init; }
    }
}

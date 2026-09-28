using System.Text.Json;

namespace TodoAgent.Api.Agent.Llm;

/// <summary>
/// The conversation as the loop sees it: four kinds of message, no provider
/// vocabulary. Each <see cref="ILlmClient"/> translates these to and from its
/// own wire format.
/// </summary>
public abstract record LlmMessage;

/// <summary>Instructions to the model. The loop puts one at the front of every request.</summary>
public sealed record SystemMessage(string Text) : LlmMessage;

/// <summary>Something the human typed.</summary>
public sealed record UserMessage(string Text) : LlmMessage;

/// <summary>
/// One model turn: optional text plus zero or more tool calls. <see cref="Payload"/>
/// is the provider's own representation of the turn, echoed back verbatim on the
/// next request. Providers attach reasoning blocks and signatures to a turn and
/// require them unchanged, so the neutral fields are a projection for the loop and
/// the UI, never a substitute for the original.
/// </summary>
public sealed record AssistantMessage(
    string? Text,
    IReadOnlyList<ToolCall> ToolCalls,
    ProviderPayload? Payload = null) : LlmMessage;

/// <summary>The outcome of one tool call, keyed by the id the model gave the call.</summary>
public sealed record ToolResultMessage(string ToolCallId, string Content, bool IsError) : LlmMessage;

/// <summary>
/// Opaque provider data. <see cref="Provider"/> names who wrote it so a client
/// never replays another client's format.
/// </summary>
public sealed record ProviderPayload(string Provider, JsonElement Content);

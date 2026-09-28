namespace TodoAgent.Api.Agent.Llm;

public enum StopReason
{
    /// <summary>The model finished its turn.</summary>
    EndTurn,

    /// <summary>The model wants tool results before it can continue.</summary>
    ToolUse,

    /// <summary>The output hit the token limit and is probably truncated.</summary>
    MaxTokens,

    /// <summary>The provider declined to answer.</summary>
    Refusal,

    Other,
}

/// <summary>
/// One model turn, provider-neutral. <see cref="Payload"/> carries the provider's
/// original representation so it can be echoed back exactly (see <see cref="AssistantMessage"/>).
/// </summary>
public sealed record LlmResponse(
    string? Text,
    IReadOnlyList<ToolCall> ToolCalls,
    StopReason StopReason,
    ProviderPayload? Payload = null)
{
    public AssistantMessage ToAssistantMessage() => new(Text, ToolCalls, Payload);
}

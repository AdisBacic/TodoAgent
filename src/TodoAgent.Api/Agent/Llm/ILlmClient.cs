namespace TodoAgent.Api.Agent.Llm;

/// <summary>
/// The only thing the agent loop knows about a language model: give it the
/// conversation and the available tools, get back text and/or tool calls.
/// </summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct);
}

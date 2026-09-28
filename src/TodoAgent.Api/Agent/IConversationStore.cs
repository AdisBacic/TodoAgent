using TodoAgent.Api.Agent.Llm;

namespace TodoAgent.Api.Agent;

/// <summary>
/// Message history per conversation. The loop appends every message the moment
/// it exists, user text, model turns and tool results alike, so a run can be
/// resumed from the last persisted step rather than restarted.
/// </summary>
public interface IConversationStore
{
    Task<IReadOnlyList<LlmMessage>> GetMessagesAsync(string conversationId, CancellationToken ct);

    Task AppendAsync(string conversationId, LlmMessage message, CancellationToken ct);
}

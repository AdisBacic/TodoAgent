using System.Collections.Concurrent;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Agent.Llm;

namespace TodoAgent.Api.Features.Chat;

/// <summary>
/// Chapter 2 persistence for conversations: a list per id, in memory. Appends
/// are the unit of persistence, which is what lets the loop resume.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<LlmMessage>> _conversations = new();

    public Task<IReadOnlyList<LlmMessage>> GetMessagesAsync(string conversationId, CancellationToken ct)
    {
        var messages = _conversations.GetOrAdd(conversationId, _ => []);
        lock (messages)
        {
            return Task.FromResult<IReadOnlyList<LlmMessage>>(messages.ToList());
        }
    }

    public Task AppendAsync(string conversationId, LlmMessage message, CancellationToken ct)
    {
        var messages = _conversations.GetOrAdd(conversationId, _ => []);
        lock (messages)
        {
            messages.Add(message);
        }

        return Task.CompletedTask;
    }
}

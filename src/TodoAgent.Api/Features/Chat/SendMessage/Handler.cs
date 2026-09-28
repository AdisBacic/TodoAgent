using System.Runtime.CompilerServices;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Api.Features.Shared;

namespace TodoAgent.Api.Features.Chat.SendMessage;

/// <summary>
/// The chat use case: record what the user said, run the agent, stream what
/// happens. It returns events, not HTTP; the endpoint decides they go out as SSE.
/// </summary>
public sealed class SendMessageHandler(IConversationStore conversations, AgentLoop loop)
{
    public IAsyncEnumerable<AgentEvent> Handle(string conversationId, SendMessageRequest request, CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["text"] = ["Text is required."] });
        }

        return Stream(conversationId, text, ct);
    }

    private async IAsyncEnumerable<AgentEvent> Stream(
        string conversationId,
        string text,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // A previous run may have been cut off between a tool call and its result
        // (closed tab, crashed process). Finishing it is just running the loop
        // again: it resumes from the last persisted step. Only then does the new
        // user message go on the record.
        var history = await conversations.GetMessagesAsync(conversationId, ct);
        if (AgentLoop.PendingToolCalls(history).Count > 0)
        {
            await foreach (var interrupted in loop.RunAsync(conversationId, ct))
            {
                yield return interrupted;
            }
        }

        await conversations.AppendAsync(conversationId, new UserMessage(text), ct);

        await foreach (var agentEvent in loop.RunAsync(conversationId, ct))
        {
            yield return agentEvent;
        }
    }
}

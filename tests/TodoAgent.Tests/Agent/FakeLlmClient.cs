using System.Text.Json;
using TodoAgent.Api.Agent.Llm;

namespace TodoAgent.Tests.Agent;

/// <summary>
/// A scripted model. Each call to <see cref="CompleteAsync"/> pops the next
/// scripted response and records what it was asked, so a test can assert both
/// the events the loop produced and the conversation the model saw.
/// </summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly Queue<Func<LlmResponse>> _script = new();
    private int _nextCallId;

    public List<(IReadOnlyList<LlmMessage> Messages, IReadOnlyList<ToolDefinition> Tools)> Calls { get; } = [];

    /// <summary>A plain text answer: the loop should stop after this.</summary>
    public FakeLlmClient Reply(string text)
    {
        _script.Enqueue(() => new LlmResponse(text, [], StopReason.EndTurn));
        return this;
    }

    /// <summary>One tool call, optionally with text alongside it.</summary>
    public FakeLlmClient CallTool(string name, string inputJson = "{}", string? text = null) =>
        CallTools([(name, inputJson)], text);

    /// <summary>Several tool calls in one turn, as a model doing parallel tool use would.</summary>
    public FakeLlmClient CallTools(IReadOnlyList<(string Name, string InputJson)> calls, string? text = null)
    {
        _script.Enqueue(() =>
        {
            var toolCalls = calls
                .Select(c => new ToolCall($"call-{++_nextCallId}", c.Name, JsonDocument.Parse(c.InputJson).RootElement.Clone()))
                .ToList();
            return new LlmResponse(text, toolCalls, StopReason.ToolUse);
        });
        return this;
    }

    public FakeLlmClient Refuse()
    {
        _script.Enqueue(() => new LlmResponse(null, [], StopReason.Refusal));
        return this;
    }

    public FakeLlmClient Throw(Exception exception)
    {
        _script.Enqueue(() => throw exception);
        return this;
    }

    public Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct)
    {
        Calls.Add((messages.ToList(), tools.ToList()));

        if (_script.Count == 0)
        {
            throw new InvalidOperationException($"FakeLlmClient has no scripted response for call #{Calls.Count}.");
        }

        return Task.FromResult(_script.Dequeue()());
    }
}

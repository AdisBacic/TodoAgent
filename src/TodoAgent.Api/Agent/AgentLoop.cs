using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using TodoAgent.Api.Agent.Llm;

namespace TodoAgent.Api.Agent;

/// <summary>
/// The agent. Ask the model; if it asked for tools, run them and ask again; stop
/// when it answers in plain text or the iteration cap fires. Provider-agnostic
/// (it only sees <see cref="ILlmClient"/>) and transport-agnostic (it only sees
/// <see cref="IToolProvider"/>). It yields events and never touches HTTP.
/// </summary>
public sealed class AgentLoop(
    ILlmClient llm,
    IToolProvider toolProvider,
    IConversationStore conversations,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILogger<AgentLoop> logger)
{
    /// <summary>
    /// Runs the conversation forward from whatever is persisted. Calling it again
    /// after an interruption resumes: tool calls that already have a stored result
    /// are not re-run, and the model is only asked once every call has one.
    /// </summary>
    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string conversationId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var maxIterations = options.Value.MaxIterations;
        var timeout = TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(timeout);
        var token = budget.Token;

        using var scope = logger.BeginScope("Conversation {ConversationId}", conversationId);

        var tools = await toolProvider.GetToolsAsync(token);
        var definitions = tools.Select(t => new ToolDefinition(t.Name, t.Description, t.InputSchema)).ToList();
        var history = (await conversations.GetMessagesAsync(conversationId, token)).ToList();

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            // Resume point: tool calls from the last model turn that have no result yet.
            var pending = PendingToolCalls(history);

            if (pending.Count == 0)
            {
                var (response, failure) = await AskModelAsync(history, definitions, ct, token);
                if (failure is not null)
                {
                    yield return new AgentEvent.Error(failure);
                    yield break;
                }

                var assistant = response!.ToAssistantMessage();
                await conversations.AppendAsync(conversationId, assistant, token);
                history.Add(assistant);

                logger.LogInformation(
                    "Iteration {Iteration}/{Max}: stop reason {StopReason}, {ToolCallCount} tool call(s)",
                    iteration, maxIterations, response.StopReason, response.ToolCalls.Count);

                if (!string.IsNullOrEmpty(response.Text))
                {
                    yield return new AgentEvent.TextDelta(response.Text);
                }

                foreach (var call in response.ToolCalls)
                {
                    yield return new AgentEvent.ToolCallRequested(call.Id, call.Name, call.Input);
                }

                if (response.StopReason == StopReason.Refusal)
                {
                    yield return new AgentEvent.Error("The model declined this request.");
                    yield break;
                }

                if (response.ToolCalls.Count == 0)
                {
                    yield return new AgentEvent.Done();
                    yield break;
                }

                pending = response.ToolCalls;
            }

            foreach (var call in pending)
            {
                var (result, failure) = await RunToolAsync(tools, call, ct, token);
                if (failure is not null)
                {
                    yield return new AgentEvent.Error(failure);
                    yield break;
                }

                var message = new ToolResultMessage(call.Id, result!.Content, result.IsError);
                await conversations.AppendAsync(conversationId, message, token);
                history.Add(message);

                yield return new AgentEvent.ToolCallCompleted(call.Id, call.Name, result.Content, result.IsError);
            }
        }

        logger.LogWarning("Stopped after {Max} iterations without a final answer", maxIterations);
        yield return new AgentEvent.MaxIterationsReached(maxIterations);
    }

    /// <summary>
    /// The tool calls in the most recent model turn that have no
    /// <see cref="ToolResultMessage"/> after them. Empty when the history ends
    /// in a user message or a text-only model turn.
    /// </summary>
    public static IReadOnlyList<ToolCall> PendingToolCalls(IReadOnlyList<LlmMessage> history)
    {
        var answered = new HashSet<string>();

        for (var i = history.Count - 1; i >= 0; i--)
        {
            switch (history[i])
            {
                case ToolResultMessage result:
                    answered.Add(result.ToolCallId);
                    break;

                case AssistantMessage assistant:
                    return assistant.ToolCalls.Where(call => !answered.Contains(call.Id)).ToList();

                default:
                    return [];
            }
        }

        return [];
    }

    // The two awaits below run under a linked token that fires for two reasons:
    // the caller went away, or the run budget expired. Only the second is worth
    // reporting as an event; nobody is listening for the first, so it propagates.

    private async Task<(LlmResponse? Response, string? Failure)> AskModelAsync(
        List<LlmMessage> history,
        List<ToolDefinition> definitions,
        CancellationToken caller,
        CancellationToken token)
    {
        // Rebuilt on every call: it carries today's date, and it is never persisted.
        var request = new List<LlmMessage>(history.Count + 1) { SystemPrompt() };
        request.AddRange(history);

        try
        {
            return (await llm.CompleteAsync(request, definitions, token), null);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            return (null, TimeoutMessage);
        }
        catch (LlmException ex)
        {
            logger.LogError(ex, "Model call failed");
            return (null, ex.Message);
        }
    }

    private async Task<(ToolResult? Result, string? Failure)> RunToolAsync(
        IReadOnlyList<ITool> tools,
        ToolCall call,
        CancellationToken caller,
        CancellationToken token)
    {
        var tool = tools.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null)
        {
            logger.LogWarning("Model asked for unknown tool {Tool}", call.Name);
            var known = string.Join(", ", tools.Select(t => t.Name));
            return (ToolResult.Error($"Unknown tool '{call.Name}'. Available tools: {known}."), null);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await tool.InvokeSafelyAsync(call.Input, token);
            logger.LogInformation(
                "Tool {Tool} ({CallId}) finished in {ElapsedMs} ms, error: {IsError}",
                call.Name, call.Id, stopwatch.ElapsedMilliseconds, result.IsError);
            return (result, null);
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            return (null, TimeoutMessage);
        }
    }

    private string TimeoutMessage => $"The run timed out after {options.Value.RequestTimeoutSeconds} seconds.";

    private SystemMessage SystemPrompt() => new(
        $"""
        You are TodoAgent, an assistant that manages the user's todo list.
        Today is {time.GetUtcNow():yyyy-MM-dd}.
        Use the tools for any change to todos; never claim a change you did not make with a tool.
        If you need a todo's id, call list_todos first.
        When you are done, confirm what you did in one short line.
        """);
}

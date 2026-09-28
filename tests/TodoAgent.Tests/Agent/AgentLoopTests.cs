using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Api.Features.Chat;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Agent;

public class AgentLoopTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeLlmClient Llm { get; } = new();
        public InMemoryConversationStore Store { get; } = new();
        public List<ITool> Tools { get; } = [];
        public AgentOptions Options { get; } = new() { MaxIterations = 8, RequestTimeoutSeconds = 30 };

        public AgentLoop Loop() => new(
            Llm,
            new InProcessToolProvider(Tools),
            Store,
            Microsoft.Extensions.Options.Options.Create(Options),
            new FixedTimeProvider(T0),
            NullLogger<AgentLoop>.Instance);

        public async Task<List<AgentEvent>> RunAsync(string conversationId = "c1", string? userText = "hello", CancellationToken ct = default)
        {
            if (userText is not null)
            {
                await Store.AppendAsync(conversationId, new UserMessage(userText), ct);
            }

            var events = new List<AgentEvent>();
            await foreach (var e in Loop().RunAsync(conversationId, ct))
            {
                events.Add(e);
            }

            return events;
        }
    }

    [Fact]
    public async Task Text_only_reply_yields_text_then_done()
    {
        var h = new Harness();
        h.Llm.Reply("Hi there.");

        var events = await h.RunAsync();

        Assert.Collection(events,
            e => Assert.Equal("Hi there.", Assert.IsType<AgentEvent.TextDelta>(e).Text),
            e => Assert.IsType<AgentEvent.Done>(e));

        var history = await h.Store.GetMessagesAsync("c1", CancellationToken.None);
        Assert.Collection(history,
            m => Assert.Equal("hello", Assert.IsType<UserMessage>(m).Text),
            m => Assert.Equal("Hi there.", Assert.IsType<AssistantMessage>(m).Text));
    }

    [Fact]
    public async Task Model_sees_system_prompt_with_date_and_tool_definitions()
    {
        var h = new Harness();
        h.Tools.Add(new RecordingTool("list_todos", "[]"));
        h.Llm.Reply("ok");

        await h.RunAsync();

        var (messages, tools) = Assert.Single(h.Llm.Calls);
        var system = Assert.IsType<SystemMessage>(messages[0]);
        Assert.Contains("2026-09-28", system.Text);
        Assert.Equal("list_todos", Assert.Single(tools).Name);
    }

    [Fact]
    public async Task Tool_call_is_run_and_its_result_fed_back_to_the_model()
    {
        var h = new Harness();
        var tool = new RecordingTool("create_todo", """{"id":1,"title":"buy milk"}""");
        h.Tools.Add(tool);
        h.Llm.CallTool("create_todo", """{"title":"buy milk"}""", text: "Adding it.").Reply("Added #1.");

        var events = await h.RunAsync(userText: "add buy milk");

        Assert.Collection(events,
            e => Assert.Equal("Adding it.", Assert.IsType<AgentEvent.TextDelta>(e).Text),
            e =>
            {
                var requested = Assert.IsType<AgentEvent.ToolCallRequested>(e);
                Assert.Equal("create_todo", requested.Name);
                Assert.Equal("buy milk", requested.Input.GetProperty("title").GetString());
            },
            e =>
            {
                var completed = Assert.IsType<AgentEvent.ToolCallCompleted>(e);
                Assert.Equal("call-1", completed.CallId);
                Assert.False(completed.IsError);
                Assert.Contains("buy milk", completed.Result);
            },
            e => Assert.Equal("Added #1.", Assert.IsType<AgentEvent.TextDelta>(e).Text),
            e => Assert.IsType<AgentEvent.Done>(e));

        Assert.Equal("buy milk", Assert.Single(tool.Inputs).GetProperty("title").GetString());

        // The second model call sees: system, user, assistant(tool call), tool result.
        var second = h.Llm.Calls[1].Messages;
        Assert.Equal(4, second.Count);
        var result = Assert.IsType<ToolResultMessage>(second[3]);
        Assert.Equal("call-1", result.ToolCallId);
        Assert.Contains("buy milk", result.Content);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Parallel_tool_calls_all_run_before_the_next_model_call()
    {
        var h = new Harness();
        h.Tools.Add(new RecordingTool("create_todo", "created"));
        h.Tools.Add(new RecordingTool("complete_todo", "completed"));
        h.Llm.CallTools([("create_todo", """{"title":"milk"}"""), ("complete_todo", """{"id":2}""")]).Reply("Both done.");

        var events = await h.RunAsync();

        Assert.Equal(2, events.OfType<AgentEvent.ToolCallCompleted>().Count());
        Assert.Equal(2, h.Llm.Calls.Count);
        var results = h.Llm.Calls[1].Messages.OfType<ToolResultMessage>().ToList();
        Assert.Equal(["call-1", "call-2"], results.Select(r => r.ToolCallId));
    }

    [Fact]
    public async Task Tool_exception_becomes_an_error_result_instead_of_crashing_the_run()
    {
        var h = new Harness();
        h.Tools.Add(new RecordingTool("delete_todo", (_, _) => throw new InvalidOperationException("Todo #9 was not found.")));
        h.Llm.CallTool("delete_todo", """{"id":9}""").Reply("It was not there.");

        var events = await h.RunAsync();

        var completed = Assert.Single(events.OfType<AgentEvent.ToolCallCompleted>());
        Assert.True(completed.IsError);
        Assert.Contains("Todo #9 was not found.", completed.Result);
        Assert.IsType<AgentEvent.Done>(events[^1]);

        var fedBack = Assert.Single(h.Llm.Calls[1].Messages.OfType<ToolResultMessage>());
        Assert.True(fedBack.IsError);
    }

    [Fact]
    public async Task Unknown_tool_name_becomes_an_error_result_listing_the_real_tools()
    {
        var h = new Harness();
        h.Tools.Add(new RecordingTool("list_todos", "[]"));
        h.Llm.CallTool("make_coffee").Reply("Sorry, no coffee.");

        var events = await h.RunAsync();

        var completed = Assert.Single(events.OfType<AgentEvent.ToolCallCompleted>());
        Assert.True(completed.IsError);
        Assert.Contains("make_coffee", completed.Result);
        Assert.Contains("list_todos", completed.Result);
    }

    [Fact]
    public async Task Iteration_cap_stops_a_model_that_never_finishes()
    {
        var h = new Harness();
        h.Options.MaxIterations = 3;
        h.Tools.Add(new RecordingTool("list_todos", "[]"));
        for (var i = 0; i < 10; i++)
        {
            h.Llm.CallTool("list_todos");
        }

        var events = await h.RunAsync();

        Assert.Equal(3, h.Llm.Calls.Count);
        Assert.Equal(3, events.OfType<AgentEvent.ToolCallCompleted>().Count());
        Assert.Equal(3, Assert.IsType<AgentEvent.MaxIterationsReached>(events[^1]).Iterations);
        Assert.DoesNotContain(events, e => e is AgentEvent.Done);
    }

    [Fact]
    public async Task Model_failure_yields_an_error_event_and_leaves_history_consistent()
    {
        var h = new Harness();
        h.Llm.Throw(new LlmException("Anthropic returned 401: bad key"));

        var events = await h.RunAsync();

        var error = Assert.IsType<AgentEvent.Error>(Assert.Single(events));
        Assert.Contains("401", error.Message);
        var history = await h.Store.GetMessagesAsync("c1", CancellationToken.None);
        Assert.Single(history);
        Assert.IsType<UserMessage>(history[0]);
    }

    [Fact]
    public async Task Unexpected_model_exception_is_still_an_error_event()
    {
        var h = new Harness();
        h.Llm.Throw(new HttpRequestException("connection refused"));

        var events = await h.RunAsync();

        var error = Assert.IsType<AgentEvent.Error>(Assert.Single(events));
        Assert.Contains("connection refused", error.Message);
    }

    [Fact]
    public async Task Refusal_yields_an_error_event()
    {
        var h = new Harness();
        h.Llm.Refuse();

        var events = await h.RunAsync();

        Assert.IsType<AgentEvent.Error>(events[^1]);
    }

    [Fact]
    public async Task Rerunning_resumes_from_the_last_persisted_step_instead_of_restarting()
    {
        // History as an interrupted run leaves it: the model asked for two tools,
        // the first result was stored, then the process died.
        var h = new Harness();
        var create = new RecordingTool("create_todo", "created");
        var complete = new RecordingTool("complete_todo", "completed");
        h.Tools.Add(create);
        h.Tools.Add(complete);
        await h.Store.AppendAsync("c1", new UserMessage("add milk and finish dentist"), CancellationToken.None);
        await h.Store.AppendAsync("c1", new AssistantMessage(null,
        [
            new ToolCall("call-a", "create_todo", JsonDocument.Parse("""{"title":"milk"}""").RootElement),
            new ToolCall("call-b", "complete_todo", JsonDocument.Parse("""{"id":2}""").RootElement),
        ]), CancellationToken.None);
        await h.Store.AppendAsync("c1", new ToolResultMessage("call-a", "created earlier", false), CancellationToken.None);
        h.Llm.Reply("Milk added, dentist done.");

        var events = await h.RunAsync(userText: null);

        Assert.Empty(create.Inputs);
        Assert.Single(complete.Inputs);
        Assert.Collection(events,
            e => Assert.Equal("call-b", Assert.IsType<AgentEvent.ToolCallCompleted>(e).CallId),
            e => Assert.IsType<AgentEvent.TextDelta>(e),
            e => Assert.IsType<AgentEvent.Done>(e));

        // The model was called once, and saw both results in order.
        var messages = Assert.Single(h.Llm.Calls).Messages;
        var results = messages.OfType<ToolResultMessage>().Select(r => r.ToolCallId).ToList();
        Assert.Equal(["call-a", "call-b"], results);
    }

    [Fact]
    public async Task Run_budget_expiring_during_a_tool_yields_a_timeout_error()
    {
        var h = new Harness();
        h.Options.RequestTimeoutSeconds = 1;
        h.Tools.Add(new RecordingTool("slow", async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ToolResult.Ok("never");
        }));
        h.Llm.CallTool("slow");

        var events = await h.RunAsync();

        var error = Assert.IsType<AgentEvent.Error>(events[^1]);
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public void PendingToolCalls_finds_only_unanswered_calls_of_the_last_turn()
    {
        var input = JsonDocument.Parse("{}").RootElement;
        List<LlmMessage> history =
        [
            new UserMessage("hi"),
            new AssistantMessage(null, [new ToolCall("old", "list_todos", input)]),
            new ToolResultMessage("old", "[]", false),
            new AssistantMessage(null, [new ToolCall("a", "create_todo", input), new ToolCall("b", "create_todo", input)]),
            new ToolResultMessage("a", "ok", false),
        ];

        var pending = AgentLoop.PendingToolCalls(history);

        Assert.Equal("b", Assert.Single(pending).Id);
        Assert.Empty(AgentLoop.PendingToolCalls([.. history, new ToolResultMessage("b", "ok", false)]));
        Assert.Empty(AgentLoop.PendingToolCalls([.. history, new UserMessage("something else")]));
    }
}

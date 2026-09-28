
## 2. Chapter 2 — what an agent loop actually is

An agent is a `while` loop around a model call. Version 1 of this project had the loop in a
console app and asked the model to answer in a home-made JSON shape,
`{thought, action, isGoalMet}`. This version keeps the loop and drops the home-made shape: the
provider's own tool-calling does the job, and does it better (see the interlude).

### The one diagram that matters

```
                      ┌────────────────────────────────────────────┐
  user text ─────────►│ AgentLoop.RunAsync(conversationId)         │
                      │                                            │
                      │  history = store.GetMessages(id)           │
                      │  loop (max N):                             │
                      │    pending = unanswered tool calls         │
                      │    if none:                                │
                      │      response = llm.Complete(history,tools)│──► ILlmClient
                      │      store.Append(assistant turn)          │
                      │      yield TextDelta / ToolCallRequested   │
                      │      if no tool calls: yield Done; return  │
                      │    for call in pending:                    │
                      │      result = tool.Invoke(call.input)      │──► IToolProvider → ITool
                      │      store.Append(tool result)             │
                      │      yield ToolCallCompleted               │
                      │  yield MaxIterationsReached                │
                      └────────────────────────────────────────────┘
                                          │ IAsyncEnumerable<AgentEvent>
                                          ▼
                      SendMessage/Endpoint.cs writes each event as SSE
```

Three interfaces, three folders:

| Interface | Lives in | Chapter 2 implementation |
|---|---|---|
| `ILlmClient` | `Agent/Llm/` | `AnthropicLlmClient`: raw `HttpClient` + `System.Text.Json` |
| `ITool` | `Agent/` | `Features/Tools/*Tool.cs`, one per slice |
| `IToolProvider` | `Agent/` | `InProcessToolProvider`: whatever `ITool`s DI has |

### The neutral types

`Agent/Llm/LlmMessage.cs` defines the conversation as the loop sees it: `SystemMessage`,
`UserMessage`, `AssistantMessage` (text plus tool calls) and `ToolResultMessage`. No provider
vocabulary anywhere. Each `ILlmClient` owns the translation both ways; `AnthropicLlmClient.cs`
is the entire Anthropic wire format in one file, property names spelled out.

One non-obvious field: `AssistantMessage.Payload`. Providers attach reasoning blocks and
signatures to a turn and require them back unchanged on the next request. So the client keeps
the provider's original content blocks and replays them verbatim; the neutral `Text` and
`ToolCalls` are a projection for the loop and the UI. The payload is tagged with the provider
name, so a client never replays another client's format.

### A tool is a thin adapter

```csharp
public sealed class CreateTodoTool(CreateTodoHandler handler) : ITool
{
    public string Name => "create_todo";
    public string Description => "Create a new todo with the given title.";
    public JsonElement InputSchema { get; } = ToolJson.Schema("""{ "type": "object", ... }""");

    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var request = input.Deserialize<CreateTodoRequest>(ToolJson.Options);
        var todo = await handler.Handle(request, ct);
        return ToolResult.Ok(ToolJson.Serialize(todo));
    }
}
```

That is the whole tool. It calls the same handler the HTTP endpoint calls, so the model gets the
same validation ("Title is required.") and the same idempotency as the UI. Behaviour cannot
drift between the two front doors because there is only one implementation.

### Fail-safes

- **Iteration cap.** `Agent:MaxIterations` (default 8) bounds model calls per run. When it
  fires the loop yields `MaxIterationsReached` and stops.
- **Run budget.** `Agent:RequestTimeoutSeconds` (default 120) is a linked cancellation token
  over the whole run. A timeout is reported as an `Error` event; a client disconnect is not
  reported, because nobody is listening.
- **Tool errors are content.** `ITool.InvokeSafelyAsync` turns any exception into
  `ToolResult.Error(message)`. The model reads "Todo #9 was not found." and decides what to do.
  An unknown tool name gets the same treatment, with the list of real tools in the message.
- **Idempotent complete**, from chapter 1, so a retried call is harmless.

### Designed for resume, even while inline

The loop never touches the HTTP response; it yields events. Every message is appended to
`IConversationStore` the moment it exists: the user's text, the model's turn (with its tool
calls), each tool result. That gives the resume property for free:

> `RunAsync(conversationId)` starts by asking "which tool calls in the last model turn have no
> result yet?" and runs exactly those. A run interrupted between a tool call and its result is
> resumed by calling `RunAsync` again. It does not restart, and it does not re-run tools that
> already have a stored result.

`SendMessageHandler` uses this: before recording a new user message it checks for an
interrupted run and finishes it first. Chapter 4 (optional) moves the loop off the request; the
loop itself will not change.

### The SSE endpoint and the timeline

`Features/Chat/SendMessage/Endpoint.cs` sets `Content-Type: text/event-stream`, then writes
each event as one `data:` line of JSON and flushes. `AgentEvent` is a closed set of records
with a `kind` discriminator (`text_delta`, `tool_call_requested`, `tool_call_completed`,
`done`, `error`, `max_iterations`), serialised by `System.Text.Json`'s polymorphism support.

The browser cannot use `EventSource` for a POST, so `useAgentStream.ts` reads the body with a
`ReadableStream` and splits frames on blank lines. Each event becomes a row in the step
timeline: your message, "→ create_todo({"title":"buy milk"})", "← {"id":1,...}", the model's
one-line confirmation. After every completed tool call the todo list reloads.

**What to read next:** `Agent/AgentLoop.cs` (it is shorter than this section), then
`Agent/Llm/AnthropicLlmClient.cs`, then `tests/TodoAgent.Tests/Agent/AgentLoopTests.cs`.

## Interlude — tool-calling vs. asking the model for JSON

Version 1 asked the model for `{"thought": "...", "action": {...}, "isGoalMet": false}` and
parsed it. It mostly worked. It also had four problems that tool-calling removes:

1. **Parsing.** Models wrap JSON in prose, in code fences, with trailing commentary. Every one
   needs a regex and a retry. With tool-calling the tool call arrives as a typed block with the
   arguments already parsed by the provider.
2. **Schemas.** The home-made shape had no way to tell the model what arguments a tool takes.
   Tool definitions carry JSON Schema; the model is trained on it; `"id": "3"` still happens,
   but rarely, and `ToolJson.Options` is lenient about it.
3. **Parallelism.** One JSON object meant one action per turn. A tool-calling turn can carry
   several calls; the loop runs them all and returns all the results together.
4. **State.** With custom JSON the loop had to decide what to feed back and how to phrase it.
   With tool results the provider defines the shape, and the conversation history is a
   faithful record the model can be resumed from.

The loop got shorter, and the model got better at using it. The cost is a provider-specific
wire format, which is exactly what `ILlmClient` isolates.

## Configuration

`appsettings.json` holds the non-secret settings:

```json
"Llm": {
  "Provider": "anthropic",
  "Anthropic": { "Model": "claude-opus-5", "BaseUrl": "https://api.anthropic.com", "MaxTokens": 8192, "Effort": "low" }
},
"Agent": { "MaxIterations": 8, "RequestTimeoutSeconds": 120 }
```

The API key is never in a file that is committed:

```bash
# locally
dotnet user-secrets set "Llm:Anthropic:ApiKey" "sk-ant-..." --project src/TodoAgent.Api
# or anywhere
export ANTHROPIC_API_KEY=sk-ant-...
```

Without a key the todo list works and the chat answers with a single `error` event telling you
what to set.

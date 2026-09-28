# TodoAgent

A todo app with two front doors to the same features: a React UI and an AI chat.
The chat is an agent that discovers tools over MCP and calls them to do work for you.

This repository is a learning project, written to be read in order. The git history
is the table of contents: each chapter below was one or two commits, and every commit
leaves the app runnable and the tests green.

| Chapter | What it adds |
|---|---|
| [0. Why vertical slices](#0-why-vertical-slices) | The shape of the codebase, and what we deliberately do not use |
| [1. A slice, dissected](#1-chapter-1--a-slice-dissected) | Four todo slices and a React list. No AI yet |
| [2. What an agent loop actually is](#2-chapter-2--what-an-agent-loop-actually-is) | `ILlmClient`, `ITool`, `AgentLoop`, streamed to the UI over SSE |
| [Interlude. Tool-calling vs. asking for JSON](#interlude--tool-calling-vs-asking-the-model-for-json) | Why the loop got simpler when we stopped parsing prose |
| [3. MCP: the loop doesn't know where tools live](#3-chapter-3--mcp-the-loop-doesnt-know-where-tools-live) | The same tools served over MCP and consumed by an MCP client. One DI line changes |
| [4. Testing an agent without an API key](#4-testing-an-agent-without-an-api-key) | A scripted fake LLM and what the loop tests actually assert |
| [5. Where to go next](#5-where-to-go-next) | SQLite, auth, external MCP servers, token streaming |
| [6. Optional chapter 4: moving the loop off the request](#6-optional-chapter-4--moving-the-loop-off-the-request) | Background execution, replay, retries |

## Running it

Prerequisites: .NET 9 SDK, Node 22.

```bash
# API on http://localhost:5080
dotnet run --project src/TodoAgent.Api

# Web on http://localhost:5173 (proxies /api to the API)
cd src/TodoAgent.Web && npm install && npm run dev

# Tests
dotnet test
```

Health check: `GET http://localhost:5080/api/health`.

The chat needs an LLM API key. See [Configuration](#configuration) once chapter 2 lands.

## 0. Why vertical slices

Most .NET sample apps are cut horizontally: a `Controllers` folder, a `Services` folder, a
`Repositories` folder, and a `Models` folder. Adding one feature means touching all four, and
reading one feature means jumping between all four. The folders describe *what kind of code*
something is, not *what it does*.

This codebase is cut vertically. A feature is a folder:

```
Features/Todos/CreateTodo/
├── Endpoint.cs    # HTTP in, HTTP out. Knows about routes and status codes.
├── Handler.cs     # The use case. Knows about todos and the store. No HTTP.
├── Request.cs     # The input shape.
└── Validator.cs   # Rules about the input, kept out of the handler body.
```

Everything the `CreateTodo` use case needs is in that folder. Things two slices genuinely
share sit one level up, at the area root: the `Todo` entity and the `ITodoStore` interface.
That is the whole rule:

> A slice owns its request, validation, handler, endpoint and tests. Slices do not reference
> each other. Cross-area sharing goes through the store or the tool, never by importing another
> slice's handler.

### What we are not using, and why

- **No MediatR.** A handler is a class with one method. `Task<TodoResponse> Handle(CreateTodoRequest, CancellationToken)`.
  The endpoint takes the handler from DI and calls it. There is no bus, no pipeline, no
  `IRequest<T>` marker. When you can see what a slice is without a library in the way, you can
  decide later whether a library earns its place.
- **No repository layer.** `ITodoStore` is the one abstraction todos need, and it exists so
  chapter 1 can be in-memory and a later chapter can be SQLite. It is not a generic
  `IRepository<T>`; it has the five methods todos use.
- **No layers.** There is no `Domain` / `Application` / `Infrastructure` split into separate
  projects. One web project, folders by feature. The agent and MCP code live in their own
  folders (`Agent/`, `Mcp/`) because they are not features; they are the machinery features
  plug into.
- **No AI framework.** LLM calls are `HttpClient` plus `System.Text.Json`. The provider wire
  formats are small, and seeing them is the point.

### Where the folders come from

```
src/TodoAgent.Api/
├── Program.cs          # composition root only: DI, endpoints, middleware
├── Features/
│   ├── Todos/          # chapter 1: the four slices, entity, store
│   ├── Chat/           # chapter 2: the SSE endpoint that runs the loop
│   └── Tools/          # chapter 2: one ITool per slice
├── Agent/              # chapter 2: the loop, events, LLM clients
└── Mcp/                # chapter 3: the loop's tools served and consumed over MCP
```

`Program.cs` is deliberately the least interesting file. It is also the only file that knows
about every slice, which makes it the map. Read it first, then read one slice.

## 1. Chapter 1 — a slice, dissected

Four slices: `ListTodos`, `CreateTodo`, `CompleteTodo`, `DeleteTodo`. Each is a folder under
`Features/Todos/`. They share three files at the area root: `Todo.cs` (the entity and its typed
id), `ITodoStore.cs` (the store interface) and `TodoResponse.cs` (the wire shape every slice
returns). `InMemoryTodoStore.cs` is chapter 1's persistence: a dictionary.

### The one diagram that matters

```
HTTP request
    │
    ▼
Endpoint.cs ──── knows: route, status code, Location header
    │  calls handler.Handle(request, ct)
    ▼
Handler.cs ───── knows: the use case, validation, the store
    │  throws ValidationException / NotFoundException on failure
    ▼
ITodoStore ───── knows: nothing about HTTP or use cases
```

The arrows only point down. An endpoint never touches the store; a handler never sees
`HttpContext`. That is what makes chapter 2 possible: a tool can call the same handler the
endpoint calls, and get the same validation and the same errors.

### Walk through `CreateTodo`

1. **`Request.cs`**: `record CreateTodoRequest(string? Title)`. Nullable, because JSON binding
   cannot promise a value. The type tells the truth; the validator enforces the rule.
2. **`Validator.cs`**: a pure static function from request to a dictionary of errors. No
   exceptions, no store. You can read it in ten seconds and test it without a host.
3. **`Handler.cs`**: validate, throw `ValidationException` if there are errors, add to the
   store, map to `TodoResponse`. That is the entire use case.
4. **`Endpoint.cs`**: `MapPost("/", ...)`, call the handler, return `201 Created` with a
   `Location` header. It is registered from `Program.cs` with one line:
   `CreateTodoEndpoint.Map(todos)`.

### Errors without a result type

Handlers throw two exceptions, `ValidationException` and `NotFoundException`, both in
`Features/Shared/`. One endpoint filter (`DomainExceptionFilter`), attached to the `/api/todos`
route group in `Program.cs`, maps them to `400` and `404` problem+json responses. That keeps
every endpoint to three lines and means the agent layer in chapter 2 gets a plain exception
with a readable message it can hand to the model. A filter rather than the global exception
middleware because these are expected outcomes: they should not be logged as crashes.

The trade-off is real: exceptions for expected outcomes are slower and less explicit than a
`Result<T>` type. For four slices with two failure modes, the exception version is shorter and
easier to read. If the slices grow to a dozen failure modes, revisit it.

### Idempotency

`CompleteTodoHandler` treats "complete an already-completed todo" as success and changes
nothing. That is a deliberate contract for chapter 2: a model that retries a tool call, or
calls it twice, cannot corrupt state or get a confusing error. `DeleteTodo` does return `404`
on a missing id; a delete that silently succeeds hides bugs.

### Tests

`tests/TodoAgent.Tests/Features/Todos/` has one class per slice. Each class tests the handler
directly against a real `InMemoryTodoStore` and a `FixedTimeProvider`, then tests the endpoint
through `WebApplicationFactory<Program>` for status codes, headers and JSON shape. No mocks;
the store is fast enough to use as is.

### The React side

`src/TodoAgent.Web/src/features/todos/` mirrors the API: `api.ts` has one function per
endpoint, `useTodos.ts` holds the list and re-reads it after every action, `TodoList.tsx` and
`TodoItem.tsx` render. Re-reading after every action instead of patching local state is a
small inefficiency bought on purpose: from chapter 2 the agent changes todos behind the UI's
back, and "reload from the server" is the simplest correct thing.

**What to read next:** `Program.cs` top to bottom, then `Features/Todos/CreateTodo/` in the
order above, then `CreateTodoTests.cs`.

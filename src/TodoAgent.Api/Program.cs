using Microsoft.Extensions.Options;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Api.Features.Chat;
using TodoAgent.Api.Features.Chat.SendMessage;
using TodoAgent.Api.Features.Shared;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.CompleteTodo;
using TodoAgent.Api.Features.Todos.CreateTodo;
using TodoAgent.Api.Features.Todos.DeleteTodo;
using TodoAgent.Api.Features.Todos.ListTodos;
using TodoAgent.Api.Features.Tools;
using TodoAgent.Api.Mcp;

// Program.cs is the composition root: it wires dependencies, maps endpoints and
// starts the host. It contains no behaviour of its own. Every feature lives in
// a slice under Features/, and every slice is registered here by name so this
// file doubles as the table of contents for the whole API.

var builder = WebApplication.CreateBuilder(args);

// Problem+json for anything that fails, including unhandled 500s outside Development.
builder.Services.AddProblemDetails();

// Todos: one store, one handler per slice.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITodoStore, InMemoryTodoStore>();
builder.Services.AddTransient<ListTodosHandler>();
builder.Services.AddTransient<CreateTodoHandler>();
builder.Services.AddTransient<CompleteTodoHandler>();
builder.Services.AddTransient<DeleteTodoHandler>();

// LLM: configuration picks the provider. Nothing outside Agent/Llm knows which one.
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.AddHttpClient<AnthropicLlmClient>();
builder.Services.AddHttpClient<OpenAiLlmClient>();
builder.Services.AddTransient<ILlmClient>(sp =>
    sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider.ToLowerInvariant() switch
    {
        "openai" => sp.GetRequiredService<OpenAiLlmClient>(),
        _ => sp.GetRequiredService<AnthropicLlmClient>(),
    });

// Tools: one ITool per todo slice, each a thin adapter over that slice's handler.
builder.Services.AddTransient<ITool, ListTodosTool>();
builder.Services.AddTransient<ITool, CreateTodoTool>();
builder.Services.AddTransient<ITool, CompleteTodoTool>();
builder.Services.AddTransient<ITool, DeleteTodoTool>();

// Where the loop gets its tools. Chapter 2: straight from DI. Chapter 3: from
// MCP servers, this app's own included. This is the one line that changes; the
// loop, the tools and the slices are untouched either way.
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));
if (builder.Configuration["Tools:Provider"] == "mcp")
{
    builder.Services.AddSingleton<IToolProvider, McpToolProvider>();
}
else
{
    builder.Services.AddSingleton<IToolProvider, InProcessToolProvider>();
}

// The MCP server: the same four tools, served at /mcp for any MCP client.
builder.Services.AddTodoMcpServer();

// Agent: the loop, its fail-safe settings, and where conversations live.
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddTransient<AgentLoop>();
builder.Services.AddTransient<SendMessageHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

// Every todo endpoint sits in one group: one prefix, one filter that turns
// ValidationException and NotFoundException into 400 and 404 responses.
var todos = app.MapGroup("/api/todos")
    .WithTags("Todos")
    .AddEndpointFilter<DomainExceptionFilter>();
ListTodosEndpoint.Map(todos);
CreateTodoEndpoint.Map(todos);
CompleteTodoEndpoint.Map(todos);
DeleteTodoEndpoint.Map(todos);

// The chat's one endpoint streams the agent's steps as Server-Sent Events.
var chat = app.MapGroup("/api/chat")
    .WithTags("Chat")
    .AddEndpointFilter<DomainExceptionFilter>();
SendMessageEndpoint.Map(chat);

app.MapMcp("/mcp");

app.Run();

// Exposes the implicit Program class so the test project can host the app
// in-memory with WebApplicationFactory<Program>.
public partial class Program;

using TodoAgent.Api.Agent;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Api.Features.Chat;
using TodoAgent.Api.Features.Tools;
using TodoAgent.Api.Features.Shared;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.CompleteTodo;
using TodoAgent.Api.Features.Todos.CreateTodo;
using TodoAgent.Api.Features.Todos.DeleteTodo;
using TodoAgent.Api.Features.Todos.ListTodos;

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
builder.Services.AddTransient<ILlmClient>(sp => sp.GetRequiredService<AnthropicLlmClient>());

// Tools: one ITool per todo slice, each a thin adapter over that slice's handler.
builder.Services.AddTransient<ITool, ListTodosTool>();
builder.Services.AddTransient<ITool, CreateTodoTool>();
builder.Services.AddTransient<ITool, CompleteTodoTool>();
builder.Services.AddTransient<ITool, DeleteTodoTool>();
builder.Services.AddSingleton<IToolProvider, InProcessToolProvider>();

// Agent: the loop, its fail-safe settings, and where conversations live.
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddTransient<AgentLoop>();

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

app.Run();

// Exposes the implicit Program class so the test project can host the app
// in-memory with WebApplicationFactory<Program>.
public partial class Program;

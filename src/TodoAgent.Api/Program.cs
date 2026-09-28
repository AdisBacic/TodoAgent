using TodoAgent.Api.Features.Shared;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.CreateTodo;
using TodoAgent.Api.Features.Todos.ListTodos;

// Program.cs is the composition root: it wires dependencies, maps endpoints and
// starts the host. It contains no behaviour of its own. Every feature lives in
// a slice under Features/, and every slice is registered here by name so this
// file doubles as the table of contents for the whole API.

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting: domain exceptions become problem+json responses.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Todos: one store, one handler per slice.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITodoStore, InMemoryTodoStore>();
builder.Services.AddTransient<ListTodosHandler>();
builder.Services.AddTransient<CreateTodoHandler>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

var todos = app.MapGroup("/api/todos").WithTags("Todos");
ListTodosEndpoint.Map(todos);
CreateTodoEndpoint.Map(todos);

app.Run();

// Exposes the implicit Program class so the test project can host the app
// in-memory with WebApplicationFactory<Program>.
public partial class Program;

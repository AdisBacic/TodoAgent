namespace TodoAgent.Api.Features.Todos.CreateTodo;

public static class CreateTodoEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder todos)
    {
        todos.MapPost("/", async (CreateTodoRequest request, CreateTodoHandler handler, CancellationToken ct) =>
        {
            var todo = await handler.Handle(request, ct);
            return Results.Created($"/api/todos/{todo.Id}", todo);
        })
        .WithName("CreateTodo");

        return todos;
    }
}

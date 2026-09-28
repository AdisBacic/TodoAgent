namespace TodoAgent.Api.Features.Todos.CompleteTodo;

public static class CompleteTodoEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder todos)
    {
        todos.MapPost("/{id:int}/complete", async (int id, CompleteTodoHandler handler, CancellationToken ct) =>
        {
            var todo = await handler.Handle(new CompleteTodoRequest(id), ct);
            return Results.Ok(todo);
        })
        .WithName("CompleteTodo");

        return todos;
    }
}

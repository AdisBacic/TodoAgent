namespace TodoAgent.Api.Features.Todos.DeleteTodo;

public static class DeleteTodoEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder todos)
    {
        todos.MapDelete("/{id:int}", async (int id, DeleteTodoHandler handler, CancellationToken ct) =>
        {
            await handler.Handle(new DeleteTodoRequest(id), ct);
            return Results.NoContent();
        })
        .WithName("DeleteTodo");

        return todos;
    }
}

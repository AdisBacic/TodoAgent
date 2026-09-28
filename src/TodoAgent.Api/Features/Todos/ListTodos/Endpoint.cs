namespace TodoAgent.Api.Features.Todos.ListTodos;

public static class ListTodosEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder todos)
    {
        todos.MapGet("/", (ListTodosHandler handler, CancellationToken ct) => handler.Handle(ct))
             .WithName("ListTodos");

        return todos;
    }
}

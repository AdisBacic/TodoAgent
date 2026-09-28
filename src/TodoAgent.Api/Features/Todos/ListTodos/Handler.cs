namespace TodoAgent.Api.Features.Todos.ListTodos;

public sealed class ListTodosHandler(ITodoStore store)
{
    public async Task<ListTodosResponse> Handle(CancellationToken ct)
    {
        var todos = await store.ListAsync(ct);
        return new ListTodosResponse(todos.Select(TodoResponse.From).ToList());
    }
}

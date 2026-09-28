using TodoAgent.Api.Features.Shared;

namespace TodoAgent.Api.Features.Todos.CompleteTodo;

public sealed class CompleteTodoHandler(ITodoStore store, TimeProvider time)
{
    public async Task<TodoResponse> Handle(CompleteTodoRequest request, CancellationToken ct)
    {
        var id = new TodoId(request.Id);
        var todo = await store.FindAsync(id, ct)
            ?? throw new NotFoundException($"Todo #{id} was not found.");

        // Idempotency guard: completing an already-completed todo succeeds and
        // changes nothing. A retrying client, or a model that calls the tool
        // twice, cannot corrupt state or get a confusing error.
        if (!todo.IsCompleted)
        {
            todo = todo.Complete(time.GetUtcNow());
            await store.SaveAsync(todo, ct);
        }

        return TodoResponse.From(todo);
    }
}

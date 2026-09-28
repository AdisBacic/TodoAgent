using TodoAgent.Api.Features.Shared;

namespace TodoAgent.Api.Features.Todos.DeleteTodo;

public sealed class DeleteTodoHandler(ITodoStore store)
{
    public async Task Handle(DeleteTodoRequest request, CancellationToken ct)
    {
        var id = new TodoId(request.Id);
        if (!await store.RemoveAsync(id, ct))
        {
            throw new NotFoundException($"Todo #{id} was not found.");
        }
    }
}

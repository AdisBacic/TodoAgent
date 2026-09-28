using System.Collections.Concurrent;

namespace TodoAgent.Api.Features.Todos;

/// <summary>
/// Chapter 1 persistence: a dictionary. Ids are sequential so they are short
/// and stable within a run. <see cref="TimeProvider"/> is injected so tests can
/// assert on timestamps.
/// </summary>
public sealed class InMemoryTodoStore(TimeProvider time) : ITodoStore
{
    private readonly ConcurrentDictionary<TodoId, Todo> _todos = new();
    private int _lastId;

    public Task<IReadOnlyList<Todo>> ListAsync(CancellationToken ct)
    {
        IReadOnlyList<Todo> ordered = _todos.Values.OrderBy(t => t.Id.Value).ToList();
        return Task.FromResult(ordered);
    }

    public Task<Todo?> FindAsync(TodoId id, CancellationToken ct) =>
        Task.FromResult(_todos.GetValueOrDefault(id));

    public Task<Todo> AddAsync(string title, CancellationToken ct)
    {
        var id = new TodoId(Interlocked.Increment(ref _lastId));
        var todo = new Todo(id, title, IsCompleted: false, CreatedAt: time.GetUtcNow(), CompletedAt: null);
        _todos[id] = todo;
        return Task.FromResult(todo);
    }

    public Task SaveAsync(Todo todo, CancellationToken ct)
    {
        _todos[todo.Id] = todo;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(TodoId id, CancellationToken ct) =>
        Task.FromResult(_todos.TryRemove(id, out _));
}

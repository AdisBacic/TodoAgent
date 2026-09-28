namespace TodoAgent.Api.Features.Todos;

/// <summary>
/// The one abstraction todos need. It is not a generic repository: it has
/// exactly the operations the four slices use, and nothing else.
/// </summary>
public interface ITodoStore
{
    Task<IReadOnlyList<Todo>> ListAsync(CancellationToken ct);

    Task<Todo?> FindAsync(TodoId id, CancellationToken ct);

    /// <summary>Creates a todo. The store assigns the id and the creation time.</summary>
    Task<Todo> AddAsync(string title, CancellationToken ct);

    Task SaveAsync(Todo todo, CancellationToken ct);

    /// <summary>Returns false when no todo with that id existed.</summary>
    Task<bool> RemoveAsync(TodoId id, CancellationToken ct);
}

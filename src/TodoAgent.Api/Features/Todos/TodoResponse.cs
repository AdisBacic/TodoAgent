namespace TodoAgent.Api.Features.Todos;

/// <summary>
/// The wire shape of a todo. Shared by every slice that returns one, so it sits
/// at the area root rather than inside a slice.
/// </summary>
public sealed record TodoResponse(
    int Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static TodoResponse From(Todo todo) =>
        new(todo.Id.Value, todo.Title, todo.IsCompleted, todo.CreatedAt, todo.CompletedAt);
}

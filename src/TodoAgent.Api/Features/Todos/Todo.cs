namespace TodoAgent.Api.Features.Todos;

/// <summary>
/// A typed id so a todo id cannot be confused with any other int. Small ints
/// (rather than GUIDs) are deliberate: humans and language models both refer
/// to "#3" far more reliably than to a 36-character string.
/// </summary>
public readonly record struct TodoId(int Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>
/// The entity, immutable. State changes return a new instance, which keeps the
/// in-memory store trivially thread-safe and makes handlers easy to read.
/// </summary>
public sealed record Todo(
    TodoId Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public Todo Complete(DateTimeOffset now) => this with { IsCompleted = true, CompletedAt = now };
}

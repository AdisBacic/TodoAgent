namespace TodoAgent.Api.Features.Todos.CreateTodo;

/// <summary>
/// Title is nullable on purpose: JSON binding cannot promise a value, so the
/// type tells the truth and the validator enforces the rule.
/// </summary>
public sealed record CreateTodoRequest(string? Title);

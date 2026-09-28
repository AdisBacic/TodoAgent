namespace TodoAgent.Api.Features.Todos.ListTodos;

public sealed record ListTodosResponse(IReadOnlyList<TodoResponse> Todos);

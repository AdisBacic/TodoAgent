using TodoAgent.Api.Features.Shared;

namespace TodoAgent.Api.Features.Todos.CreateTodo;

/// <summary>
/// The use case. No HTTP, no JSON, no status codes: validate, act, return.
/// Both the endpoint and (in chapter 2) the tool call exactly this.
/// </summary>
public sealed class CreateTodoHandler(ITodoStore store)
{
    public async Task<TodoResponse> Handle(CreateTodoRequest request, CancellationToken ct)
    {
        var errors = CreateTodoValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var todo = await store.AddAsync(request.Title!.Trim(), ct);
        return TodoResponse.From(todo);
    }
}

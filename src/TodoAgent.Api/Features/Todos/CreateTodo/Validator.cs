namespace TodoAgent.Api.Features.Todos.CreateTodo;

/// <summary>
/// Pure function from request to errors. It does not throw and does not touch
/// the store, so it can be read and tested in isolation.
/// </summary>
public static class CreateTodoValidator
{
    public const int MaxTitleLength = 200;

    public static IReadOnlyDictionary<string, string[]> Validate(CreateTodoRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = ["Title is required."];
        }
        else if (request.Title.Trim().Length > MaxTitleLength)
        {
            errors["title"] = [$"Title must be at most {MaxTitleLength} characters."];
        }

        return errors;
    }
}

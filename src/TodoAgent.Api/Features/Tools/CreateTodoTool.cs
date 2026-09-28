using System.Text.Json;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Features.Todos.CreateTodo;

namespace TodoAgent.Api.Features.Tools;

/// <summary>
/// Wraps <see cref="CreateTodoHandler"/>. The same validation the HTTP endpoint
/// gets: a blank title is a <c>ValidationException</c>, which reaches the model as
/// an error result.
/// </summary>
public sealed class CreateTodoTool(CreateTodoHandler handler) : ITool
{
    public string Name => "create_todo";

    public string Description => "Create a new todo with the given title.";

    public JsonElement InputSchema { get; } = ToolJson.Schema("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "What needs doing, e.g. 'buy milk'." }
          },
          "required": ["title"],
          "additionalProperties": false
        }
        """);

    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var request = input.Deserialize<CreateTodoRequest>(ToolJson.Options) ?? new CreateTodoRequest(null);
        var todo = await handler.Handle(request, ct);
        return ToolResult.Ok(ToolJson.Serialize(todo));
    }
}

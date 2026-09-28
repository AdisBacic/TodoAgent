using System.Text.Json;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Features.Todos.ListTodos;

namespace TodoAgent.Api.Features.Tools;

/// <summary>Wraps <see cref="ListTodosHandler"/>. The model reads ids from this before acting on a todo.</summary>
public sealed class ListTodosTool(ListTodosHandler handler) : ITool
{
    public string Name => "list_todos";

    public string Description =>
        "List every todo with its id, title and whether it is completed. " +
        "Call this first when you need a todo's id.";

    public JsonElement InputSchema { get; } = ToolJson.Schema("""
        { "type": "object", "properties": {}, "additionalProperties": false }
        """);

    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var response = await handler.Handle(ct);
        return ToolResult.Ok(ToolJson.Serialize(response.Todos));
    }
}

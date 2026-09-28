using System.Text.Json;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Features.Todos.DeleteTodo;

namespace TodoAgent.Api.Features.Tools;

/// <summary>Wraps <see cref="DeleteTodoHandler"/>.</summary>
public sealed class DeleteTodoTool(DeleteTodoHandler handler) : ITool
{
    public string Name => "delete_todo";

    public string Description => "Delete a todo permanently.";

    public JsonElement InputSchema { get; } = ToolJson.Schema("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "integer", "description": "The todo id, as returned by list_todos." }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """);

    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var request = input.Deserialize<DeleteTodoRequest>(ToolJson.Options) ?? new DeleteTodoRequest(0);
        await handler.Handle(request, ct);
        return ToolResult.Ok($"Deleted todo #{request.Id}.");
    }
}

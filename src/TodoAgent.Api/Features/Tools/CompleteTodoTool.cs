using System.Text.Json;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Features.Todos.CompleteTodo;

namespace TodoAgent.Api.Features.Tools;

/// <summary>Wraps <see cref="CompleteTodoHandler"/>, idempotency included.</summary>
public sealed class CompleteTodoTool(CompleteTodoHandler handler) : ITool
{
    public string Name => "complete_todo";

    public string Description =>
        "Mark a todo as completed. Succeeds even if it was already completed.";

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
        var request = input.Deserialize<CompleteTodoRequest>(ToolJson.Options) ?? new CompleteTodoRequest(0);
        var todo = await handler.Handle(request, ct);
        return ToolResult.Ok(ToolJson.Serialize(todo));
    }
}

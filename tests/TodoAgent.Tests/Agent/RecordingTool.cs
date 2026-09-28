using System.Text.Json;
using TodoAgent.Api.Agent;

namespace TodoAgent.Tests.Agent;

/// <summary>A tool with scripted behaviour that records every input it was given.</summary>
public sealed class RecordingTool(string name, Func<JsonElement, CancellationToken, Task<ToolResult>> behaviour) : ITool
{
    public RecordingTool(string name, string result)
        : this(name, (_, _) => Task.FromResult(ToolResult.Ok(result)))
    {
    }

    public List<JsonElement> Inputs { get; } = [];

    public string Name => name;

    public string Description => $"Test tool {name}";

    public JsonElement InputSchema { get; } = ToolJson.Schema("""{"type":"object","properties":{}}""");

    public Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        Inputs.Add(input.Clone());
        return behaviour(input, ct);
    }
}

using System.Text.Json;

namespace TodoAgent.Api.Agent;

/// <summary>
/// The tool contract. A tool is a name, a description and a JSON Schema the model
/// reads, plus one method the loop calls. Chapter 2 implements it in-process;
/// chapter 3 implements it over MCP. The loop cannot tell the difference.
/// </summary>
public interface ITool
{
    string Name { get; }

    string Description { get; }

    /// <summary>JSON Schema for <see cref="InvokeAsync"/>'s input. Sent to the model verbatim.</summary>
    JsonElement InputSchema { get; }

    Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct);
}

public static class ToolExtensions
{
    /// <summary>
    /// Invokes the tool and turns any failure into <see cref="ToolResult.Error"/>.
    /// This is the single fail-safe that keeps a bad tool call from crashing a
    /// run: the model gets the message and decides what to do next. Cancellation
    /// is not a tool failure and still propagates.
    /// </summary>
    public static async Task<ToolResult> InvokeSafelyAsync(this ITool tool, JsonElement input, CancellationToken ct)
    {
        try
        {
            return await tool.InvokeAsync(input, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"{tool.Name} failed: {ex.Message}");
        }
    }
}

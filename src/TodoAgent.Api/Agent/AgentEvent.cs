using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoAgent.Api.Agent;

/// <summary>
/// Everything the loop tells the outside world, as a closed set of records. The
/// SSE endpoint serialises these as-is; the <c>kind</c> discriminator is what the
/// browser switches on.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextDelta), "text_delta")]
[JsonDerivedType(typeof(ToolCallRequested), "tool_call_requested")]
[JsonDerivedType(typeof(ToolCallCompleted), "tool_call_completed")]
[JsonDerivedType(typeof(Done), "done")]
[JsonDerivedType(typeof(Error), "error")]
[JsonDerivedType(typeof(MaxIterationsReached), "max_iterations")]
public abstract record AgentEvent
{
    /// <summary>Assistant text. One event per model turn today; per token once streaming lands.</summary>
    public sealed record TextDelta(string Text) : AgentEvent;

    /// <summary>The model asked for a tool. Emitted before the tool runs.</summary>
    public sealed record ToolCallRequested(string CallId, string Name, JsonElement Input) : AgentEvent;

    /// <summary>The tool ran. <see cref="Result"/> is exactly what the model will see.</summary>
    public sealed record ToolCallCompleted(string CallId, string Name, string Result, bool IsError) : AgentEvent;

    /// <summary>The model finished without asking for more tools.</summary>
    public sealed record Done : AgentEvent;

    /// <summary>The run stopped because the model call failed, timed out, or was refused.</summary>
    public sealed record Error(string Message) : AgentEvent;

    /// <summary>The fail-safe fired: the model kept asking for tools past the cap.</summary>
    public sealed record MaxIterationsReached(int Iterations) : AgentEvent;
}

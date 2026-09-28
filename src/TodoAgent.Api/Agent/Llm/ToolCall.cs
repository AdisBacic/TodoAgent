using System.Text.Json;

namespace TodoAgent.Api.Agent.Llm;

/// <summary>
/// A request from the model to run a tool. <see cref="Id"/> is the model's own
/// id for the call; results are matched to calls by it.
/// </summary>
public sealed record ToolCall(string Id, string Name, JsonElement Input);

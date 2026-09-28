using System.Text.Json;

namespace TodoAgent.Api.Agent.Llm;

/// <summary>What the model is told about a tool. <see cref="InputSchema"/> is JSON Schema.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema);

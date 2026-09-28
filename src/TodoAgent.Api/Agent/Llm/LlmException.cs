namespace TodoAgent.Api.Agent.Llm;

/// <summary>A provider call failed: bad configuration, a non-2xx response, or an unparseable body.</summary>
public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);

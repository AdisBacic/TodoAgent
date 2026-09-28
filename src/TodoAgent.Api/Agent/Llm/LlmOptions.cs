namespace TodoAgent.Api.Agent.Llm;

/// <summary>Bound from the "Llm" section of appsettings. API keys come from user secrets or the environment.</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>"anthropic" or "openai". Picks the <see cref="ILlmClient"/> in Program.cs.</summary>
    public string Provider { get; set; } = "anthropic";

    public AnthropicOptions Anthropic { get; set; } = new();
}

public sealed class AnthropicOptions
{
    /// <summary>Falls back to the ANTHROPIC_API_KEY environment variable when unset.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    public int MaxTokens { get; set; } = 8192;

    /// <summary>Reasoning effort: low, medium, high, xhigh or max. Null sends the provider default.</summary>
    public string? Effort { get; set; } = "low";
}

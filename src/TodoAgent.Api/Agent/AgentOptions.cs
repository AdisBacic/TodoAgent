namespace TodoAgent.Api.Agent;

/// <summary>Bound from the "Agent" section of appsettings.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Maximum model calls per run. The fail-safe against a model that never stops asking for tools.</summary>
    public int MaxIterations { get; set; } = 8;

    /// <summary>Wall-clock budget for one run, model calls and tool calls included.</summary>
    public int RequestTimeoutSeconds { get; set; } = 120;
}

namespace TodoAgent.Api.Mcp;

/// <summary>
/// Bound from the "Mcp" section of appsettings: the servers the agent should
/// take its tools from. Each entry is either an HTTP endpoint or a command to
/// launch and talk to over stdio.
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public List<McpServerEntry> Servers { get; set; } = [];
}

public sealed class McpServerEntry
{
    public string Name { get; set; } = "";

    /// <summary>Streamable HTTP endpoint, e.g. http://localhost:5080/mcp.</summary>
    public string? Url { get; set; }

    /// <summary>Executable to launch for a stdio server, e.g. "npx".</summary>
    public string? Command { get; set; }

    public string[] Arguments { get; set; } = [];
}

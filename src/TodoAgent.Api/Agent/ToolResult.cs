namespace TodoAgent.Api.Agent;

/// <summary>
/// What a tool hands back to the model: text, and whether it is an error. Errors
/// are content, not exceptions, so the model can read them and recover.
/// </summary>
public sealed record ToolResult(string Content, bool IsError)
{
    public static ToolResult Ok(string content) => new(content, IsError: false);

    public static ToolResult Error(string message) => new(message, IsError: true);
}

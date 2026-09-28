namespace TodoAgent.Api.Agent;

/// <summary>
/// Where the loop gets its tools. Swapping the implementation is the whole of
/// chapter 3.
/// </summary>
public interface IToolProvider
{
    Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct);
}

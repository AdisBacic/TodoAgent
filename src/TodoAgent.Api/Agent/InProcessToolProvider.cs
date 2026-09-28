namespace TodoAgent.Api.Agent;

/// <summary>Chapter 2: the tools are whatever <see cref="ITool"/>s are registered in DI.</summary>
public sealed class InProcessToolProvider(IEnumerable<ITool> tools) : IToolProvider
{
    private readonly IReadOnlyList<ITool> _tools = tools.ToList();

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct) => Task.FromResult(_tools);
}

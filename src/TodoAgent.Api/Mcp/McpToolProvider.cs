using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TodoAgent.Api.Agent;

namespace TodoAgent.Api.Mcp;

/// <summary>
/// Chapter 3: an <see cref="IToolProvider"/> whose tools live on MCP servers.
/// It connects on first use, calls <c>tools/list</c> on each configured server
/// and wraps every entry as an <see cref="ITool"/> whose invoke is <c>tools/call</c>.
/// The loop never learns that anything changed.
/// </summary>
public sealed class McpToolProvider(
    IOptions<McpOptions> options,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    ILogger<McpToolProvider> logger) : IToolProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _connecting = new(1, 1);
    private readonly List<McpClient> _clients = [];
    private IReadOnlyList<ITool>? _tools;

    public async Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct)
    {
        if (_tools is not null)
        {
            return _tools;
        }

        await _connecting.WaitAsync(ct);
        try
        {
            if (_tools is not null)
            {
                return _tools;
            }

            var tools = new List<ITool>();
            foreach (var server in options.Value.Servers)
            {
                var client = await McpClient.CreateAsync(CreateTransport(server), loggerFactory: loggerFactory, cancellationToken: ct);
                _clients.Add(client);

                var discovered = await client.ListToolsAsync(cancellationToken: ct);
                tools.AddRange(discovered.Select(tool => new McpTool(tool)));

                logger.LogInformation(
                    "MCP server {Server} offers {Count} tool(s): {Tools}",
                    server.Name, discovered.Count, string.Join(", ", discovered.Select(t => t.Name)));
            }

            _tools = tools;
            return tools;
        }
        finally
        {
            _connecting.Release();
        }
    }

    private IClientTransport CreateTransport(McpServerEntry server) => server switch
    {
        { Url: { Length: > 0 } url } => new HttpClientTransport(
            new HttpClientTransportOptions { Name = server.Name, Endpoint = new Uri(url) },
            httpClientFactory.CreateClient("mcp"),
            loggerFactory,
            ownsHttpClient: true),

        { Command: { Length: > 0 } command } => new StdioClientTransport(
            new StdioClientTransportOptions { Name = server.Name, Command = command, Arguments = server.Arguments },
            loggerFactory),

        _ => throw new InvalidOperationException($"MCP server '{server.Name}' needs either a Url or a Command."),
    };

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>An <see cref="ITool"/> whose implementation is on the other end of an MCP session.</summary>
    private sealed class McpTool(McpClientTool tool) : ITool
    {
        public string Name => tool.Name;

        public string Description => tool.Description;

        public JsonElement InputSchema => tool.JsonSchema;

        public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct)
        {
            var arguments = input.ValueKind == JsonValueKind.Object
                ? input.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value)
                : new Dictionary<string, object?>();

            var result = await tool.CallAsync(arguments, cancellationToken: ct);

            var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            return new ToolResult(text, result.IsError ?? false);
        }
    }
}

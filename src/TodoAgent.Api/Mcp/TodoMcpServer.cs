using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TodoAgent.Api.Agent;

namespace TodoAgent.Api.Mcp;

/// <summary>
/// Chapter 3: the same <see cref="ITool"/>s the loop uses in-process, served over
/// MCP. Two request handlers, mapping one to one: <c>tools/list</c> returns each
/// tool's name, description and schema; <c>tools/call</c> invokes one by name and
/// returns its text. The tools themselves do not change.
/// </summary>
public static class TodoMcpServer
{
    public static IServiceCollection AddTodoMcpServer(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "todo-agent", Version = "1.0.0" };
                options.ServerInstructions = "Tools for managing the user's todo list.";
            })
            .WithHttpTransport()
            .WithListToolsHandler(ListToolsAsync)
            .WithCallToolHandler(CallToolAsync);

        return services;
    }

    private static async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken ct)
    {
        var tools = await LocalToolsAsync(request, ct);

        return new ListToolsResult
        {
            Tools = tools.Select(tool => new Tool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.InputSchema,
            }).ToList(),
        };
    }

    private static async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken ct)
    {
        var name = request.Params?.Name ?? "";
        var tools = await LocalToolsAsync(request, ct);
        var tool = tools.FirstOrDefault(t => t.Name == name)
            ?? throw new McpException($"Unknown tool '{name}'.");

        // MCP arguments arrive as a dictionary; ITool takes the JSON object.
        var input = System.Text.Json.JsonSerializer.SerializeToElement(
            request.Params?.Arguments ?? new Dictionary<string, System.Text.Json.JsonElement>());

        var result = await tool.InvokeSafelyAsync(input, ct);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Content }],
            IsError = result.IsError,
        };
    }

    // The in-process tools come from the same DI registrations chapter 2 made.
    // The MCP server is one more caller of them, next to InProcessToolProvider.
    private static Task<IReadOnlyList<ITool>> LocalToolsAsync<T>(RequestContext<T> request, CancellationToken ct)
    {
        var services = request.Services ?? request.Server.Services
            ?? throw new InvalidOperationException("No service provider on the MCP request.");
        var provider = new InProcessToolProvider(services.GetRequiredService<IEnumerable<ITool>>());
        return provider.GetToolsAsync(ct);
    }
}

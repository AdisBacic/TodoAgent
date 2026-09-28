using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Api.Features.Todos.ListTodos;
using TodoAgent.Api.Mcp;
using TodoAgent.Tests.Agent;

namespace TodoAgent.Tests.Mcp;

/// <summary>
/// Chapter 3 end to end, in one process: the app hosts the MCP server at /mcp,
/// and McpToolProvider connects to it through the test server's in-memory
/// handler. No sockets, no API key, real protocol.
/// </summary>
public class McpRoundTripTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Hands the MCP client an HttpClient that talks to the in-memory test server.</summary>
    private sealed class TestServerHttpClientFactory(Func<HttpMessageHandler> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler());
    }

    private (WebApplicationFactory<Program> App, FakeLlmClient Llm) CreateApp(bool toolsOverMcp)
    {
        var llm = new FakeLlmClient();
        WebApplicationFactory<Program>? app = null;
        app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tools:Provider", toolsOverMcp ? "mcp" : "inprocess");
            builder.UseSetting("Mcp:Servers:0:Name", "self");
            builder.UseSetting("Mcp:Servers:0:Url", "http://localhost/mcp");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlmClient>();
                services.AddSingleton<ILlmClient>(llm);
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new TestServerHttpClientFactory(() => app!.Server.CreateHandler()));
            });
        });
        return (app, llm);
    }

    private static McpToolProvider ProviderFor(WebApplicationFactory<Program> app) => new(
        Options.Create(new McpOptions { Servers = [new McpServerEntry { Name = "self", Url = "http://localhost/mcp" }] }),
        new TestServerHttpClientFactory(() => app.Server.CreateHandler()),
        NullLoggerFactory.Instance,
        NullLogger<McpToolProvider>.Instance);

    [Fact]
    public async Task Tools_listed_over_mcp_match_the_in_process_tools_exactly()
    {
        var (app, _) = CreateApp(toolsOverMcp: false);
        using var _client = app.CreateClient(); // starts the server
        var local = await new InProcessToolProvider(app.Services.GetRequiredService<IEnumerable<ITool>>())
            .GetToolsAsync(CancellationToken.None);

        await using var provider = ProviderFor(app);
        var remote = await provider.GetToolsAsync(CancellationToken.None);

        Assert.Equal(local.Select(t => t.Name), remote.Select(t => t.Name));
        foreach (var (l, r) in local.Zip(remote))
        {
            Assert.Equal(l.Description, r.Description);
            Assert.Equal(JsonSerializer.Serialize(l.InputSchema), JsonSerializer.Serialize(r.InputSchema));
        }
    }

    [Fact]
    public async Task Calling_a_tool_over_mcp_changes_the_real_store()
    {
        var (app, _) = CreateApp(toolsOverMcp: false);
        using var client = app.CreateClient();
        await using var provider = ProviderFor(app);
        var tools = await provider.GetToolsAsync(CancellationToken.None);
        var create = tools.Single(t => t.Name == "create_todo");

        var result = await create.InvokeAsync(JsonDocument.Parse("""{"title":"over mcp"}""").RootElement, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("over mcp", result.Content);
        var todos = await client.GetFromJsonAsync<ListTodosResponse>("/api/todos");
        Assert.Contains(todos!.Todos, t => t.Title == "over mcp");
    }

    [Fact]
    public async Task Validation_errors_arrive_as_error_results_not_protocol_errors()
    {
        var (app, _) = CreateApp(toolsOverMcp: false);
        using var _client = app.CreateClient();
        await using var provider = ProviderFor(app);
        var tools = await provider.GetToolsAsync(CancellationToken.None);
        var create = tools.Single(t => t.Name == "create_todo");

        var result = await create.InvokeAsync(JsonDocument.Parse("""{"title":" "}""").RootElement, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Title is required.", result.Content);
    }

    [Fact]
    public async Task The_chat_runs_unchanged_with_tools_served_over_mcp()
    {
        // Tools:Provider=mcp is the one-line swap. Same loop, same slices, same
        // scripted model; the tool calls now cross an MCP session.
        var (app, llm) = CreateApp(toolsOverMcp: true);
        using var client = app.CreateClient();
        llm.CallTool("create_todo", """{"title":"via mcp"}""").Reply("Added via MCP.");

        var response = await client.PostAsJsonAsync("/api/chat/mcp-conv/messages", new { text = "add via mcp" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"kind\":\"tool_call_completed\"", body);
        Assert.Contains("\"isError\":false", body);
        Assert.Contains("Added via MCP.", body);
        var todos = await client.GetFromJsonAsync<ListTodosResponse>("/api/todos");
        Assert.Contains(todos!.Todos, t => t.Title == "via mcp");
        Assert.Equal("create_todo", Assert.Single(llm.Calls[0].Tools, t => t.Name == "create_todo").Name);
    }
}

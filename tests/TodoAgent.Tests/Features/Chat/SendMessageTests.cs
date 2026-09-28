using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Api.Features.Todos.ListTodos;
using TodoAgent.Tests.Agent;

namespace TodoAgent.Tests.Features.Chat;

/// <summary>
/// The whole chapter-2 path through real HTTP: SSE endpoint, handler, loop, the
/// real tools and the real todo store. Only the model is scripted.
/// </summary>
public class SendMessageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private (HttpClient Client, FakeLlmClient Llm) CreateClient()
    {
        var llm = new FakeLlmClient();
        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlmClient>();
                services.AddSingleton<ILlmClient>(llm);
            })).CreateClient();
        return (client, llm);
    }

    private static async Task<(HttpResponseMessage Response, List<JsonElement> Events)> SendAsync(HttpClient client, string conversationId, string text)
    {
        var response = await client.PostAsJsonAsync($"/api/chat/{conversationId}/messages", new { text });
        var body = await response.Content.ReadAsStringAsync();
        var events = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(frame => frame.Trim())
            .Where(frame => frame.StartsWith("data:"))
            .Select(frame => JsonDocument.Parse(frame["data:".Length..]).RootElement)
            .ToList();
        return (response, events);
    }

    [Fact]
    public async Task Streams_the_run_as_server_sent_events_and_really_changes_todos()
    {
        var (client, llm) = CreateClient();
        llm.CallTool("create_todo", """{"title":"buy milk"}""", text: "On it.").Reply("Added buy milk.");

        var (response, events) = await SendAsync(client, "conv-1", "add buy milk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(
            ["text_delta", "tool_call_requested", "tool_call_completed", "text_delta", "done"],
            events.Select(e => e.GetProperty("kind").GetString()));

        var completed = events[2];
        Assert.False(completed.GetProperty("isError").GetBoolean());
        Assert.Contains("buy milk", completed.GetProperty("result").GetString());

        var todos = await client.GetFromJsonAsync<ListTodosResponse>("/api/todos");
        Assert.Contains(todos!.Todos, t => t.Title == "buy milk");
    }

    [Fact]
    public async Task Blank_text_is_a_400_not_a_stream()
    {
        var (client, _) = CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat/conv-2/messages", new { text = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Later_messages_in_a_conversation_carry_the_earlier_turns()
    {
        var (client, llm) = CreateClient();
        llm.Reply("Hello!").Reply("Still here.");

        await SendAsync(client, "conv-3", "hi");
        await SendAsync(client, "conv-3", "you there?");

        var second = llm.Calls[1].Messages;
        Assert.Collection(second,
            m => Assert.IsType<SystemMessage>(m),
            m => Assert.Equal("hi", Assert.IsType<UserMessage>(m).Text),
            m => Assert.Equal("Hello!", Assert.IsType<AssistantMessage>(m).Text),
            m => Assert.Equal("you there?", Assert.IsType<UserMessage>(m).Text));
    }

    [Fact]
    public async Task Model_errors_arrive_as_an_error_event_with_a_200_stream()
    {
        var (client, llm) = CreateClient();
        llm.Throw(new LlmException("No Anthropic API key."));

        var (response, events) = await SendAsync(client, "conv-4", "add x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var error = Assert.Single(events);
        Assert.Equal("error", error.GetProperty("kind").GetString());
        Assert.Contains("API key", error.GetProperty("message").GetString());
    }
}

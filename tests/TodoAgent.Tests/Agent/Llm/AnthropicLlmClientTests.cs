using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Agent.Llm;

public class AnthropicLlmClientTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","properties":{"title":{"type":"string"}},"required":["title"]}
        """).RootElement.Clone();

    private const string ToolUseResponse = """
        {
          "id": "msg_1",
          "type": "message",
          "role": "assistant",
          "content": [
            {"type": "thinking", "thinking": "", "signature": "sig-abc"},
            {"type": "text", "text": "Adding it now."},
            {"type": "tool_use", "id": "toolu_1", "name": "create_todo", "input": {"title": "buy milk"}}
          ],
          "stop_reason": "tool_use",
          "usage": {"input_tokens": 10, "output_tokens": 20}
        }
        """;

    private static (AnthropicLlmClient Client, StubHttpMessageHandler Http) Create(string? apiKey = "test-key")
    {
        var http = new StubHttpMessageHandler();
        var options = Options.Create(new LlmOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = apiKey, Model = "claude-test", MaxTokens = 123, Effort = "low" },
        });
        var client = new AnthropicLlmClient(new HttpClient(http), options, NullLogger<AnthropicLlmClient>.Instance);
        return (client, http);
    }

    [Fact]
    public async Task Parses_text_tool_calls_stop_reason_and_keeps_the_raw_turn()
    {
        var (client, http) = Create();
        http.Respond(ToolUseResponse);

        var response = await client.CompleteAsync(
            [new SystemMessage("sys"), new UserMessage("add buy milk")],
            [new ToolDefinition("create_todo", "Create a todo", Schema)],
            CancellationToken.None);

        Assert.Equal("Adding it now.", response.Text);
        Assert.Equal(StopReason.ToolUse, response.StopReason);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("toolu_1", call.Id);
        Assert.Equal("create_todo", call.Name);
        Assert.Equal("buy milk", call.Input.GetProperty("title").GetString());
        Assert.Equal(AnthropicLlmClient.ProviderName, response.Payload!.Provider);
        Assert.Equal(3, response.Payload.Content.GetArrayLength());
    }

    [Fact]
    public async Task Sends_headers_system_prompt_tools_and_effort()
    {
        var (client, http) = Create();
        http.Respond(ToolUseResponse);

        await client.CompleteAsync(
            [new SystemMessage("You manage todos."), new UserMessage("hi")],
            [new ToolDefinition("create_todo", "Create a todo", Schema)],
            CancellationToken.None);

        var (request, body) = Assert.Single(http.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("claude-test", json.GetProperty("model").GetString());
        Assert.Equal(123, json.GetProperty("max_tokens").GetInt32());
        Assert.Equal("You manage todos.", json.GetProperty("system").GetString());
        Assert.Equal("low", json.GetProperty("output_config").GetProperty("effort").GetString());
        var tool = json.GetProperty("tools")[0];
        Assert.Equal("create_todo", tool.GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
        var user = json.GetProperty("messages")[0];
        Assert.Equal("user", user.GetProperty("role").GetString());
        Assert.Equal("hi", user.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Replays_the_assistant_turn_verbatim_and_groups_tool_results()
    {
        var (client, http) = Create();
        http.Respond(ToolUseResponse).Respond("""
            {"content":[{"type":"text","text":"Done."}],"stop_reason":"end_turn"}
            """);

        var first = await client.CompleteAsync([new UserMessage("add buy milk")], [], CancellationToken.None);
        var history = new List<LlmMessage>
        {
            new UserMessage("add buy milk"),
            first.ToAssistantMessage(),
            new ToolResultMessage("toolu_1", "{\"id\":1}", IsError: false),
            new ToolResultMessage("toolu_2", "boom", IsError: true),
        };

        var second = await client.CompleteAsync(history, [], CancellationToken.None);

        Assert.Equal("Done.", second.Text);
        Assert.Equal(StopReason.EndTurn, second.StopReason);
        var messages = JsonDocument.Parse(http.Requests[1].Body).RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());

        var assistant = messages[1];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        var blocks = assistant.GetProperty("content");
        Assert.Equal("thinking", blocks[0].GetProperty("type").GetString());
        Assert.Equal("sig-abc", blocks[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", blocks[2].GetProperty("type").GetString());

        var results = messages[2];
        Assert.Equal("user", results.GetProperty("role").GetString());
        Assert.Equal(2, results.GetProperty("content").GetArrayLength());
        Assert.Equal("toolu_1", results.GetProperty("content")[0].GetProperty("tool_use_id").GetString());
        Assert.True(results.GetProperty("content")[1].GetProperty("is_error").GetBoolean());
    }

    [Fact]
    public async Task Rebuilds_assistant_turns_that_did_not_come_from_this_provider()
    {
        var (client, http) = Create();
        http.Respond("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn"}""");
        var scripted = new AssistantMessage("Calling.", [new ToolCall("call-1", "list_todos", JsonDocument.Parse("{}").RootElement)]);

        await client.CompleteAsync(
            [new UserMessage("list"), scripted, new ToolResultMessage("call-1", "[]", false)],
            [],
            CancellationToken.None);

        var assistant = JsonDocument.Parse(http.Requests[0].Body).RootElement.GetProperty("messages")[1];
        Assert.Equal("text", assistant.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", assistant.GetProperty("content")[1].GetProperty("type").GetString());
        Assert.Equal("call-1", assistant.GetProperty("content")[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Missing_api_key_is_an_LlmException_not_a_crash()
    {
        var (client, _) = Create(apiKey: null);
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        try
        {
            var ex = await Assert.ThrowsAsync<LlmException>(() =>
                client.CompleteAsync([new UserMessage("hi")], [], CancellationToken.None));
            Assert.Contains("ANTHROPIC_API_KEY", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }
    }

    [Fact]
    public async Task Non_success_status_is_an_LlmException_with_the_body()
    {
        var (client, http) = Create();
        http.Respond("""{"error":{"type":"authentication_error","message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<LlmException>(() =>
            client.CompleteAsync([new UserMessage("hi")], [], CancellationToken.None));

        Assert.Contains("401", ex.Message);
        Assert.Contains("invalid x-api-key", ex.Message);
    }
}

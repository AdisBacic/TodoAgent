using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoAgent.Api.Agent.Llm;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Agent.Llm;

public class OpenAiLlmClientTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {"type":"object","properties":{"title":{"type":"string"}},"required":["title"]}
        """).RootElement.Clone();

    private const string ToolCallResponse = """
        {
          "id": "chatcmpl-1",
          "choices": [{
            "index": 0,
            "message": {
              "role": "assistant",
              "content": null,
              "reasoning_content": "kept as-is on replay",
              "tool_calls": [{
                "id": "call_1",
                "type": "function",
                "function": {"name": "create_todo", "arguments": "{\"title\": \"buy milk\"}"}
              }]
            },
            "finish_reason": "tool_calls"
          }]
        }
        """;

    private static (OpenAiLlmClient Client, StubHttpMessageHandler Http) Create(string? apiKey = "sk-test")
    {
        var http = new StubHttpMessageHandler();
        var options = Options.Create(new LlmOptions
        {
            OpenAi = new OpenAiOptions { ApiKey = apiKey, Model = "llama-test", BaseUrl = "http://localhost:11434/v1/" },
        });
        return (new OpenAiLlmClient(new HttpClient(http), options, NullLogger<OpenAiLlmClient>.Instance), http);
    }

    [Fact]
    public async Task Parses_tool_calls_whose_arguments_are_a_json_string()
    {
        var (client, http) = Create();
        http.Respond(ToolCallResponse);

        var response = await client.CompleteAsync([new UserMessage("add buy milk")], [new ToolDefinition("create_todo", "Create", Schema)], CancellationToken.None);

        Assert.Null(response.Text);
        Assert.Equal(StopReason.ToolUse, response.StopReason);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("create_todo", call.Name);
        Assert.Equal("buy milk", call.Input.GetProperty("title").GetString());
        Assert.Equal(OpenAiLlmClient.ProviderName, response.Payload!.Provider);
        Assert.Equal("kept as-is on replay", response.Payload.Content.GetProperty("reasoning_content").GetString());
    }

    [Fact]
    public async Task Sends_bearer_token_function_tools_and_system_role()
    {
        var (client, http) = Create();
        http.Respond("""{"choices":[{"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}]}""");

        var response = await client.CompleteAsync(
            [new SystemMessage("You manage todos."), new UserMessage("hi")],
            [new ToolDefinition("create_todo", "Create a todo", Schema)],
            CancellationToken.None);

        Assert.Equal("hi", response.Text);
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        var (request, body) = Assert.Single(http.Requests);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("llama-test", json.GetProperty("model").GetString());
        Assert.False(json.TryGetProperty("max_tokens", out _));
        var tool = json.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("create_todo", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());
        Assert.Equal("system", json.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("You manage todos.", json.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Replays_the_assistant_message_verbatim_and_sends_one_tool_message_per_result()
    {
        var (client, http) = Create();
        http.Respond(ToolCallResponse).Respond("""{"choices":[{"message":{"role":"assistant","content":"Done."},"finish_reason":"stop"}]}""");
        var first = await client.CompleteAsync([new UserMessage("add buy milk")], [], CancellationToken.None);

        await client.CompleteAsync(
        [
            new UserMessage("add buy milk"),
            first.ToAssistantMessage(),
            new ToolResultMessage("call_1", "{\"id\":1}", IsError: false),
            new ToolResultMessage("call_2", "boom", IsError: true),
        ], [], CancellationToken.None);

        var messages = JsonDocument.Parse(http.Requests[1].Body).RootElement.GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("kept as-is on replay", messages[1].GetProperty("reasoning_content").GetString());
        Assert.Equal("call_1", messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("tool", messages[2].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_call_id").GetString());
        Assert.Equal("Error: boom", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Rebuilds_assistant_turns_from_other_providers_with_string_arguments()
    {
        var (client, http) = Create();
        http.Respond("""{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""");
        var scripted = new AssistantMessage("Calling.", [new ToolCall("call-9", "list_todos", JsonDocument.Parse("{}").RootElement)]);

        await client.CompleteAsync([new UserMessage("list"), scripted, new ToolResultMessage("call-9", "[]", false)], [], CancellationToken.None);

        var assistant = JsonDocument.Parse(http.Requests[0].Body).RootElement.GetProperty("messages")[1];
        Assert.Equal("Calling.", assistant.GetProperty("content").GetString());
        var call = assistant.GetProperty("tool_calls")[0];
        Assert.Equal("function", call.GetProperty("type").GetString());
        Assert.Equal("{}", call.GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task No_key_means_no_authorization_header_which_is_what_ollama_wants()
    {
        var (client, http) = Create(apiKey: null);
        var previous = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        try
        {
            http.Respond("""{"choices":[{"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}]}""");

            await client.CompleteAsync([new UserMessage("hi")], [], CancellationToken.None);

            Assert.Null(http.Requests[0].Request.Headers.Authorization);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previous);
        }
    }

    [Fact]
    public async Task Malformed_arguments_are_passed_back_to_the_model_instead_of_crashing()
    {
        var (client, http) = Create();
        http.Respond("""
            {"choices":[{"message":{"role":"assistant","tool_calls":[{"id":"c","type":"function","function":{"name":"create_todo","arguments":"{not json"}}]},"finish_reason":"tool_calls"}]}
            """);

        var response = await client.CompleteAsync([new UserMessage("x")], [], CancellationToken.None);

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("{not json", call.Input.GetProperty("malformed_arguments").GetString());
    }
}

using System.Text.Json;
using TodoAgent.Api.Agent;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.CompleteTodo;
using TodoAgent.Api.Features.Todos.CreateTodo;
using TodoAgent.Api.Features.Todos.DeleteTodo;
using TodoAgent.Api.Features.Todos.ListTodos;
using TodoAgent.Api.Features.Tools;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Features.Tools;

/// <summary>
/// The tools are thin adapters, so these tests check the adapting: input JSON in,
/// the slice's behaviour (validation included) out, as text the model can read.
/// </summary>
public class TodoToolsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static (InMemoryTodoStore Store, List<ITool> Tools) Build()
    {
        var clock = new FixedTimeProvider(T0);
        var store = new InMemoryTodoStore(clock);
        List<ITool> tools =
        [
            new ListTodosTool(new ListTodosHandler(store)),
            new CreateTodoTool(new CreateTodoHandler(store)),
            new CompleteTodoTool(new CompleteTodoHandler(store, clock)),
            new DeleteTodoTool(new DeleteTodoHandler(store)),
        ];
        return (store, tools);
    }

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Every_tool_has_a_unique_name_and_an_object_schema()
    {
        var (_, tools) = Build();

        Assert.Equal(["list_todos", "create_todo", "complete_todo", "delete_todo"], tools.Select(t => t.Name));
        Assert.All(tools, t => Assert.Equal("object", t.InputSchema.GetProperty("type").GetString()));
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public async Task Create_then_list_round_trips_through_the_same_store()
    {
        var (_, tools) = Build();

        var created = await tools[1].InvokeAsync(Input("""{"title":"buy milk"}"""), CancellationToken.None);
        var listed = await tools[0].InvokeAsync(Input("{}"), CancellationToken.None);

        Assert.False(created.IsError);
        Assert.Equal(1, JsonDocument.Parse(created.Content).RootElement.GetProperty("id").GetInt32());
        var todos = JsonDocument.Parse(listed.Content).RootElement;
        Assert.Equal("buy milk", todos[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Create_with_blank_title_is_the_slice_validation_error_as_a_tool_error()
    {
        var (_, tools) = Build();

        var result = await tools[1].InvokeSafelyAsync(Input("""{"title":"   "}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Title is required.", result.Content);
    }

    [Fact]
    public async Task Complete_accepts_the_id_as_a_string_and_is_idempotent()
    {
        var (_, tools) = Build();
        await tools[1].InvokeAsync(Input("""{"title":"dentist"}"""), CancellationToken.None);

        var first = await tools[2].InvokeAsync(Input("""{"id":"1"}"""), CancellationToken.None);
        var second = await tools[2].InvokeAsync(Input("""{"id":1}"""), CancellationToken.None);

        Assert.Equal(first.Content, second.Content);
        Assert.True(JsonDocument.Parse(second.Content).RootElement.GetProperty("isCompleted").GetBoolean());
    }

    [Fact]
    public async Task Delete_unknown_id_is_a_not_found_tool_error()
    {
        var (_, tools) = Build();

        var result = await tools[3].InvokeSafelyAsync(Input("""{"id":42}"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("#42", result.Content);
    }

    [Fact]
    public async Task Delete_removes_the_todo()
    {
        var (store, tools) = Build();
        await tools[1].InvokeAsync(Input("""{"title":"gone"}"""), CancellationToken.None);

        var result = await tools[3].InvokeAsync(Input("""{"id":1}"""), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(await store.FindAsync(new TodoId(1), CancellationToken.None));
    }
}

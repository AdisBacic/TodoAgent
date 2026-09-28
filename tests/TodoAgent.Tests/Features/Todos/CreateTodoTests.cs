using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoAgent.Api.Features.Shared;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.CreateTodo;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Features.Todos;

public class CreateTodoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handler_creates_todo_with_trimmed_title_and_creation_time()
    {
        var store = new InMemoryTodoStore(new FixedTimeProvider(T0));
        var handler = new CreateTodoHandler(store);

        var todo = await handler.Handle(new CreateTodoRequest("  buy milk  "), CancellationToken.None);

        Assert.Equal(1, todo.Id);
        Assert.Equal("buy milk", todo.Title);
        Assert.False(todo.IsCompleted);
        Assert.Equal(T0, todo.CreatedAt);
        Assert.Null(todo.CompletedAt);
        Assert.NotNull(await store.FindAsync(new TodoId(1), CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handler_rejects_blank_title(string? title)
    {
        var handler = new CreateTodoHandler(new InMemoryTodoStore(new FixedTimeProvider(T0)));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateTodoRequest(title), CancellationToken.None));

        Assert.Equal(["Title is required."], ex.Errors["title"]);
    }

    [Fact]
    public async Task Handler_rejects_title_over_max_length()
    {
        var handler = new CreateTodoHandler(new InMemoryTodoStore(new FixedTimeProvider(T0)));
        var tooLong = new string('x', CreateTodoValidator.MaxTitleLength + 1);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new CreateTodoRequest(tooLong), CancellationToken.None));

        Assert.Contains("title", ex.Errors.Keys);
    }

    [Fact]
    public async Task Post_returns_201_with_location_and_body()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/todos", new { title = "created over http" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var todo = await response.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.Equal("created over http", todo!.Title);
        Assert.Equal($"/api/todos/{todo.Id}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Post_blank_title_returns_400_validation_problem()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/todos", new { title = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal(["Title is required."], problem!.Errors["title"]);
    }
}

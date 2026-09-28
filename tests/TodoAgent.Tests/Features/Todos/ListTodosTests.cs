using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.ListTodos;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Features.Todos;

public class ListTodosTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handler_returns_todos_in_creation_order()
    {
        var store = new InMemoryTodoStore(new FixedTimeProvider(T0));
        await store.AddAsync("first", CancellationToken.None);
        await store.AddAsync("second", CancellationToken.None);
        var handler = new ListTodosHandler(store);

        var response = await handler.Handle(CancellationToken.None);

        Assert.Equal(["first", "second"], response.Todos.Select(t => t.Title));
        Assert.Equal([1, 2], response.Todos.Select(t => t.Id));
    }

    [Fact]
    public async Task Handler_returns_empty_list_when_store_is_empty()
    {
        var handler = new ListTodosHandler(new InMemoryTodoStore(new FixedTimeProvider(T0)));

        var response = await handler.Handle(CancellationToken.None);

        Assert.Empty(response.Todos);
    }

    [Fact]
    public async Task Get_returns_200_with_todos()
    {
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/todos", new { title = "listed" });

        var response = await client.GetAsync("/api/todos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListTodosResponse>();
        Assert.Contains(body!.Todos, t => t.Title == "listed");
    }
}

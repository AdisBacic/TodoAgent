using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoAgent.Api.Features.Shared;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.DeleteTodo;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Features.Todos;

public class DeleteTodoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handler_removes_todo()
    {
        var store = new InMemoryTodoStore(new FixedTimeProvider(T0));
        var created = await store.AddAsync("remove me", CancellationToken.None);
        var handler = new DeleteTodoHandler(store);

        await handler.Handle(new DeleteTodoRequest(created.Id.Value), CancellationToken.None);

        Assert.Null(await store.FindAsync(created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Handler_throws_not_found_for_unknown_id()
    {
        var handler = new DeleteTodoHandler(new InMemoryTodoStore(new FixedTimeProvider(T0)));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteTodoRequest(999), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_returns_204_and_todo_is_gone()
    {
        using var client = factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/todos", new { title = "to delete" }))
            .Content.ReadFromJsonAsync<TodoResponse>();

        var response = await client.DeleteAsync($"/api/todos/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var again = await client.DeleteAsync($"/api/todos/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }
}

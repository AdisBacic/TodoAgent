using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoAgent.Api.Features.Shared;
using TodoAgent.Api.Features.Todos;
using TodoAgent.Api.Features.Todos.CompleteTodo;
using TodoAgent.Tests.Support;

namespace TodoAgent.Tests.Features.Todos;

public class CompleteTodoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handler_marks_todo_completed_at_current_time()
    {
        var clock = new FixedTimeProvider(T0);
        var store = new InMemoryTodoStore(clock);
        var created = await store.AddAsync("dentist", CancellationToken.None);
        clock.Now = T0.AddHours(2);
        var handler = new CompleteTodoHandler(store, clock);

        var todo = await handler.Handle(new CompleteTodoRequest(created.Id.Value), CancellationToken.None);

        Assert.True(todo.IsCompleted);
        Assert.Equal(T0.AddHours(2), todo.CompletedAt);
        Assert.True((await store.FindAsync(created.Id, CancellationToken.None))!.IsCompleted);
    }

    [Fact]
    public async Task Handler_is_idempotent_for_already_completed_todo()
    {
        var clock = new FixedTimeProvider(T0);
        var store = new InMemoryTodoStore(clock);
        var created = await store.AddAsync("dentist", CancellationToken.None);
        var handler = new CompleteTodoHandler(store, clock);
        var first = await handler.Handle(new CompleteTodoRequest(created.Id.Value), CancellationToken.None);
        clock.Now = T0.AddDays(1);

        var second = await handler.Handle(new CompleteTodoRequest(created.Id.Value), CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(T0, second.CompletedAt);
    }

    [Fact]
    public async Task Handler_throws_not_found_for_unknown_id()
    {
        var handler = new CompleteTodoHandler(new InMemoryTodoStore(new FixedTimeProvider(T0)), new FixedTimeProvider(T0));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CompleteTodoRequest(999), CancellationToken.None));

        Assert.Contains("#999", ex.Message);
    }

    [Fact]
    public async Task Post_complete_returns_200_with_completed_todo()
    {
        using var client = factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/todos", new { title = "to complete" }))
            .Content.ReadFromJsonAsync<TodoResponse>();

        var response = await client.PostAsync($"/api/todos/{created!.Id}/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var todo = await response.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.True(todo!.IsCompleted);
        Assert.NotNull(todo.CompletedAt);
    }

    [Fact]
    public async Task Post_complete_unknown_id_returns_404()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/todos/999999/complete", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

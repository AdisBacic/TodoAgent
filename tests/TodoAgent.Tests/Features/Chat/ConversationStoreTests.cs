using TodoAgent.Api.Agent.Llm;
using TodoAgent.Api.Features.Chat;

namespace TodoAgent.Tests.Features.Chat;

public class ConversationStoreTests
{
    [Fact]
    public async Task Messages_are_kept_per_conversation_in_append_order()
    {
        var store = new InMemoryConversationStore();
        await store.AppendAsync("a", new UserMessage("one"), CancellationToken.None);
        await store.AppendAsync("b", new UserMessage("other"), CancellationToken.None);
        await store.AppendAsync("a", new AssistantMessage("two", []), CancellationToken.None);

        var a = await store.GetMessagesAsync("a", CancellationToken.None);
        var b = await store.GetMessagesAsync("b", CancellationToken.None);

        Assert.Equal(2, a.Count);
        Assert.Equal("one", Assert.IsType<UserMessage>(a[0]).Text);
        Assert.Equal("two", Assert.IsType<AssistantMessage>(a[1]).Text);
        Assert.Single(b);
        Assert.Empty(await store.GetMessagesAsync("unknown", CancellationToken.None));
    }

    [Fact]
    public async Task Returned_list_is_a_snapshot()
    {
        var store = new InMemoryConversationStore();
        await store.AppendAsync("a", new UserMessage("one"), CancellationToken.None);
        var snapshot = await store.GetMessagesAsync("a", CancellationToken.None);

        await store.AppendAsync("a", new UserMessage("two"), CancellationToken.None);

        Assert.Single(snapshot);
    }
}

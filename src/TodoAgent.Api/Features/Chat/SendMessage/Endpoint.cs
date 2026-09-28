using System.Text.Json;
using TodoAgent.Api.Agent;

namespace TodoAgent.Api.Features.Chat.SendMessage;

/// <summary>
/// POST /api/chat/{conversationId}/messages, answered as a Server-Sent Events
/// stream. Each <see cref="AgentEvent"/> is one <c>data:</c> line of JSON, flushed
/// as it happens, so the browser can draw the tool calls while the run is still going.
/// </summary>
public static class SendMessageEndpoint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder Map(RouteGroupBuilder chat)
    {
        chat.MapPost("/{conversationId}/messages", async (
            string conversationId,
            SendMessageRequest request,
            SendMessageHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            // Validation happens here, before any byte of the stream is written,
            // so a bad request is still an ordinary 400.
            var events = handler.Handle(conversationId, request, ct);

            var response = http.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";

            await foreach (var agentEvent in events.WithCancellation(ct))
            {
                await response.WriteAsync($"data: {JsonSerializer.Serialize(agentEvent, Json)}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
        })
        .WithName("SendMessage");

        return chat;
    }
}

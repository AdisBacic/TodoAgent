using System.Net;
using System.Text;

namespace TodoAgent.Tests.Support;

/// <summary>
/// Captures every request and answers each with the next canned response.
/// Enough to test an HTTP client's wire format without a network.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public StubHttpMessageHandler Respond(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));

        var (status, responseBody) = _responses.Count > 0
            ? _responses.Dequeue()
            : (HttpStatusCode.InternalServerError, "no canned response left");

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}

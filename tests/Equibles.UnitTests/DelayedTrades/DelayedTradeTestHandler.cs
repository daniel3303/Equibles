using System.Net;

namespace Equibles.UnitTests.DelayedTrades;

internal sealed class DelayedTradeTestHandler(
    IEnumerable<(HttpStatusCode Status, byte[] Body, Uri FinalUri)> responses
) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode, byte[], Uri)> _responses = new(responses);
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request.RequestUri);
        if (_responses.Count == 0)
            throw new InvalidOperationException("Unexpected HTTP request.");
        var (status, body, finalUri) = _responses.Dequeue();
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(body ?? []),
            RequestMessage =
                finalUri == null ? request : new HttpRequestMessage(HttpMethod.Get, finalUri),
        };
        return Task.FromResult(response);
    }
}

using System.Net;

namespace Equibles.UnitTests.Euronext;

internal sealed class EuronextDirectoryTestHandler(IEnumerable<string> bodies) : HttpMessageHandler
{
    private readonly Queue<string> _bodies = new(bodies);
    public List<(HttpMethod Method, Uri Url, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(
            (
                request.Method,
                request.RequestUri,
                request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)
            )
        );
        if (_bodies.Count == 0)
            throw new InvalidOperationException("Unexpected HTTP request.");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_bodies.Dequeue()),
            RequestMessage = request,
        };
    }
}

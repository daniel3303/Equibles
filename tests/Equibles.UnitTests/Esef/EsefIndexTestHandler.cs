using System.Net;

namespace Equibles.UnitTests.Esef;

// Answers the index and the report addresses the index states, and nothing else: an address the service
// composed rather than took from the index shows up here as an unexpected request.
internal sealed class EsefIndexTestHandler(IReadOnlyDictionary<string, string> bodies)
    : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    // States a Content-Length larger than the body actually served, which is what tells a header pre-check
    // apart from a cap applied while reading: only the pre-check can refuse a body this small.
    public long? OverstatedContentLength { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request.RequestUri);
        var key = request.RequestUri.PathAndQuery;
        if (!bodies.TryGetValue(key, out var body))
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request }
            );
        var content = new StringContent(body);
        if (OverstatedContentLength != null)
            content.Headers.ContentLength = OverstatedContentLength;
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            }
        );
    }
}

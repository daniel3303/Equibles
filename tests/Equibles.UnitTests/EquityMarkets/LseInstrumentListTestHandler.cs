using System.Net;
using System.Net.Http.Headers;

namespace Equibles.UnitTests.EquityMarkets;

// Answers the document host: the editions it holds, each with the media type it serves them under.
internal sealed class LseInstrumentListTestHandler(
    IReadOnlyDictionary<string, (HttpStatusCode Status, string MediaType, byte[] Body)> editions
) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri Url)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add((request.Method, request.RequestUri));
        if (!editions.TryGetValue(request.RequestUri.AbsoluteUri, out var edition))
            edition = (HttpStatusCode.NotFound, "text/html", [0x3c]);
        var content = new ByteArrayContent(request.Method == HttpMethod.Head ? [] : edition.Body);
        content.Headers.ContentType = new MediaTypeHeaderValue(edition.MediaType);
        if (request.Method == HttpMethod.Head)
            content.Headers.ContentLength = edition.Body.Length;
        return Task.FromResult(
            new HttpResponseMessage(edition.Status) { Content = content, RequestMessage = request }
        );
    }
}

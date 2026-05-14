using System.Net;
using System.Text;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="SecEdgarClient.DownloadStream"/> — the streaming entry the
/// FTD importer uses for large ZIP downloads. The method takes a raw URL (no
/// SEC URL composition layer, unlike GetDocumentFileBytes / GetDocumentContent),
/// calls SendWithRetryAsync, and surfaces the response content stream the caller
/// reads from. Pins that the URL is passed through verbatim and the stream
/// contents round-trip byte-for-byte.
/// </summary>
public class SecEdgarClientDownloadStreamTests
{
    [Fact]
    public async Task DownloadStream_OkResponse_PassesUrlThroughAndStreamsResponseBytes()
    {
        var payload = Encoding.UTF8.GetBytes("zip-bytes-here");
        var url = "https://www.sec.gov/Archives/edgar/somefile.zip";
        var handler = new StubHandler(payload);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        await using var stream = await sut.DownloadStream(url);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);

        memory.ToArray().Should().Equal(payload);
        // Unlike GetDocumentFileBytes (which composes the URL), DownloadStream must
        // pass the URL through verbatim — a regression that re-routed through a
        // helper would lose the unrelated archive paths the caller depends on.
        handler.LastUrl.Should().Be(url);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public string LastUrl { get; private set; }

        public StubHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_bytes),
                }
            );
        }
    }
}

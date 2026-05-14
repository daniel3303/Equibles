using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to the existing <see cref="SecEdgarClientTests"/> entry
/// <c>GetDocumentFileBytes_RemoteReturns404_ReturnsEmptyArrayInsteadOfThrowing</c>,
/// which pins the 404 fallback. This pins the success path — bytes round-tripped
/// from a 200 — AND the SEC archive URL shape. The URL uses the UNPADDED cik
/// and the accession with dashes removed; either regression (padding the cik
/// or keeping the dashes) costs one extra 301-redirect round-trip in
/// production, which the worker's tight rate-limited loop can't afford.
/// </summary>
public class SecEdgarClientGetDocumentFileBytesTests
{
    [Fact]
    public async Task GetDocumentFileBytes_Returns200WithBytes_RoundTripsAndUsesUnpaddedCikAccessionWithoutDashes()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var handler = new CapturingHandler(payload);
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

        // Padded CIK input (leading zeros) and dashed accession — production must
        // strip both before composing the per-file URL.
        var bytes = await sut.GetDocumentFileBytes(
            cik: "0000320193",
            accessionNumber: "0000320193-25-000001",
            filename: "primary.htm"
        );

        bytes.Should().Equal(payload);

        // Expected: unpadded cik + accession-no-dashes + URL-escaped filename.
        handler
            .LastUrl.Should()
            .Be("https://www.sec.gov/Archives/edgar/data/320193/000032019325000001/primary.htm");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public string LastUrl { get; private set; }

        public CapturingHandler(byte[] bytes) => _bytes = bytes;

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

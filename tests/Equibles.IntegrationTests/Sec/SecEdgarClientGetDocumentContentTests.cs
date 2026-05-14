using System.Net;
using System.Text;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="SecEdgarClientTests"/>. That file covers <c>GetDocumentContent</c>
/// only through the <c>FilingData</c> overload's null/empty validation. The
/// <c>(accessionNumber, cik)</c> overload's happy path — URL composition via
/// <c>GetDocumentUrl</c> + <c>FormatCik</c>, response body read — is uncovered.
/// Pins the SEC URL pattern (10-digit zero-padded CIK, plain accession with no
/// dashes touched) so a regression that changed the padding length, dropped the
/// archive path, or swapped the order would surface here.
/// </summary>
public class SecEdgarClientGetDocumentContentTests
{
    [Fact]
    public async Task GetDocumentContent_AccessionAndCik_FetchesPaddedCikUrlAndReturnsBody()
    {
        // Unpadded CIK ("320193") must be 10-digit zero-padded ("0000320193") in the SEC URL.
        // A regression that drops FormatCik would 404 SEC's archive endpoint for every short
        // CIK — but the response body would still come back from a friendly handler, so the
        // URL assertion is the load-bearing check here.
        var body = "<SEC-DOCUMENT>0000320193-25-000001.txt : 20250115\n<HEADER>...";
        var handler = new CapturingHandler(body);

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

        var content = await sut.GetDocumentContent("0000320193-25-000001", "320193");

        content.Should().Be(body);
        handler
            .LastUrl.Should()
            .Be("https://www.sec.gov/Archives/edgar/data/0000320193/0000320193-25-000001.txt");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastUrl { get; private set; }

        public CapturingHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "text/plain"),
                }
            );
        }
    }
}

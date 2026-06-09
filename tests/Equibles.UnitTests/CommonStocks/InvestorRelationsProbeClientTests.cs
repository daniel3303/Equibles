using System.Net;
using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsProbeClientTests
{
    // Returns a valid IR page (title alone satisfies the validator) but reports a
    // post-redirect RequestUri far longer than the 256-char column ceiling.
    private sealed class LongFinalUrlHandler : HttpMessageHandler
    {
        private readonly string _finalUrl;

        public LongFinalUrlHandler(string finalUrl) => _finalUrl = finalUrl;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><head><title>Investor Relations</title></head><body>Welcome</body></html>",
                    System.Text.Encoding.UTF8,
                    "text/html"
                ),
                // Explicitly set so HttpClient doesn't overwrite it: simulates the
                // request having landed on an over-long URL after redirects.
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, _finalUrl),
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Discover_RedirectLandsOnUrlAboveColumnCeiling_FallsBackToProbedCandidate()
    {
        // Contract: the returned URL must stay within CommonStock.InvestorRelationsUrl's
        // 256-char ceiling. When a probe lands (after redirects) on a longer URL, the
        // short probed candidate must be returned instead of the over-long final URL.
        var overLongFinalUrl = "https://acme.com/" + new string('a', 300);
        var client = new InvestorRelationsProbeClient(
            new HttpClient(new LongFinalUrlHandler(overLongFinalUrl)),
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover("https://acme.com", ["ir"], [], CancellationToken.None);

        result!.Url.Should().Be("https://acme.com/ir");
        result.Url.Length.Should().BeLessThanOrEqualTo(256);
    }
}

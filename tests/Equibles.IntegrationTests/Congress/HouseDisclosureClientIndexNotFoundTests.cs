using System.Net;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Unit-tier <c>HouseDisclosureClientTests</c> covers only regex and parsing helpers.
/// The HTTP-driven entry path <c>GetRecentTransactions</c> is uncovered. This pins the
/// load-bearing 404 short-circuit: when the year's <c>{year}FD.zip</c> isn't published
/// yet (very common in early January), the client must log and skip rather than throw.
/// A regression that dropped the 404 check would crash the entire House sync every
/// January until that year's ZIP appeared on the disclosures-clerk endpoint.
/// </summary>
public class HouseDisclosureClientIndexNotFoundTests
{
    [Fact]
    public async Task GetRecentTransactions_IndexZipReturns404_SkipsYearAndReturnsEmptyList()
    {
        var handler = new NotFoundHandler();
        var sut = new HouseDisclosureClient(
            new HttpClient(handler),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        // Single-year window keeps the test deterministic — exactly one ZIP request.
        var transactions = await sut.GetRecentTransactions(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            CancellationToken.None
        );

        transactions.Should().BeEmpty();
        handler.RequestCount.Should().Be(1);
        // PDF fetches must never start when the index ZIP itself is 404 — a regression
        // that fell through to DownloadAndParsePtrPdf with an empty filings list would
        // still pass this empty check; the PDF-URL guard is what catches that.
        handler.LastUrl.Should().Contain("/public_disc/financial-pdfs/");
        handler.LastUrl.Should().NotContain("/public_disc/ptr-pdfs/");
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

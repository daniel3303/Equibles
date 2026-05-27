using System.Net;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeClientRealPageCassetteTests
{
    // Cassette: the actual HTML body returned by
    //   https://www.cboe.com/markets/us/options/market-statistics/daily/?dt=2020-06-15
    // captured 2026-05-27. The synthetic-HTML pin in CboeClientTests is faster
    // and easier to maintain, but it can't catch shape drift — escape rules,
    // marker spelling, brace nesting on the live page. Pin those against a
    // real captured response so a CBOE front-end rewrite that breaks the
    // parser is caught before deploy.
    [Fact]
    public async Task DownloadDailyPutCallRatios_RealCapturedPage_ExtractsAllFiveProducts()
    {
        var html = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Cboe", "Fixtures", "cboe-daily-2020-06-15.html")
        );
        var handler = new CassetteHandler(html);
        var sut = new CboeClient(
            new HttpClient(handler),
            Substitute.For<ILogger<CboeClient>>(),
            Substitute.For<IRateLimiter>()
        );
        var date = new DateOnly(2020, 6, 15);

        var result = await sut.DownloadDailyPutCallRatios(date);

        result.Should().HaveCount(5, "the captured page covers all five product types");

        // The cassette is for 2020-06-15. Ratios are the values rendered on
        // the live page at capture time; if CBOE retroactively edits a day's
        // values the assertion will need re-capturing (very rare).
        result[CboePutCallProductType.Total].PutCallRatio.Should().Be(0.88m);

        // Volumes must round-trip as longs (no decimal coercion, no nulls).
        result[CboePutCallProductType.Total].TotalVolume.Should().BeGreaterThan(0);
        result[CboePutCallProductType.Total].CallVolume.Should().BeGreaterThan(0);
        result[CboePutCallProductType.Total].PutVolume.Should().BeGreaterThan(0);
    }

    private sealed class CassetteHandler : HttpMessageHandler
    {
        private readonly string _body;

        public CassetteHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) }
            );
    }
}

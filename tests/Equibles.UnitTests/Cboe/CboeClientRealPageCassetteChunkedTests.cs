using System.Net;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeClientRealPageCassetteChunkedTests
{
    // Cassette: the actual HTML body returned by
    //   https://www.cboe.com/markets/us/options/market-statistics/daily/?dt=2026-06-08
    // captured 2026-06-09. Unlike the 2020 cassette, this page's React Server
    // Component payload is split across multiple self.__next_f.push([1,"…"])
    // script chunks, and a chunk boundary falls INSIDE the optionsData JSON —
    // the in-production regression that broke the import after 2026-06-05.
    [Fact]
    public async Task DownloadDailyPutCallRatios_ChunkedRscPayload_ExtractsAllFiveProducts()
    {
        var html = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Cboe", "Fixtures", "cboe-daily-2026-06-08.html")
        );
        var handler = new CassetteHandler(html);
        var sut = new CboeClient(
            new HttpClient(handler),
            Substitute.For<ILogger<CboeClient>>(),
            Substitute.For<IRateLimiter>()
        );
        var date = new DateOnly(2026, 6, 8);

        var result = await sut.DownloadDailyPutCallRatios(date);

        result.Should().HaveCount(5, "the captured page covers all five product types");
        result[CboePutCallProductType.Total].PutCallRatio.Should().Be(0.96m);
        result[CboePutCallProductType.Vix].PutCallRatio.Should().Be(0.73m);
        result[CboePutCallProductType.Total].CallVolume.Should().Be(7027471L);
        result[CboePutCallProductType.Total].PutVolume.Should().Be(6720631L);
        result[CboePutCallProductType.Total].TotalVolume.Should().Be(13748102L);
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

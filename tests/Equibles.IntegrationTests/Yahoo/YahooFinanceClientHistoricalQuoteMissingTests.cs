using System.Net;
using System.Text;
using Equibles.Integrations.Yahoo;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Sibling to YahooFinanceClientHistoricalTests / EmptyResult / AdjCloseNullHole /
/// RaggedArray pins. `GetHistoricalPrices` has TWO independent empty-result
/// guards: the first short-circuits on missing timestamps (covered by
/// EmptyResultTests); the second — `if (quote == null) return [];` — fires
/// when timestamps are present but the `indicators.quote` array is missing or
/// empty (Yahoo's response shape for some special tickers and a known transient
/// race during their backend migrations).
/// A refactor that dropped this second guard would NRE on the subsequent
/// `quote.Open[i]` accesses; pin the empty-quote degrade-gracefully contract.
/// </summary>
public class YahooFinanceClientHistoricalQuoteMissingTests
{
    [Fact]
    public async Task GetHistoricalPrices_TimestampsPresentButIndicatorsQuoteMissing_ReturnsEmptyListWithoutThrowing()
    {
        var unixDec23 = new DateTimeOffset(
            2024,
            12,
            23,
            0,
            0,
            0,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();
        // Timestamps present, indicators.quote array empty — the path the
        // guard exists to handle.
        var json =
            "{\"chart\":{\"result\":[{\"timestamp\":["
            + unixDec23
            + "],\"indicators\":{\"quote\":[]}}]}}";

        var handler = new StubHandler(json);
        var sut = new YahooFinanceClient(
            new HttpClient(handler),
            Substitute.For<ILogger<YahooFinanceClient>>()
        );

        var prices = await sut.GetHistoricalPrices(
            "AAPL",
            new DateOnly(2024, 12, 23),
            new DateOnly(2024, 12, 23)
        );

        prices.Should().BeEmpty();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                }
            );
    }
}

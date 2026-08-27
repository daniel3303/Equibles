using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Contract: GetLatestClosingPrices accepts up to 25 tickers per request ("Maximum 25
/// tickers per request"). The over-limit case (26 → reject) is pinned; this pins
/// the lower edge of the same boundary — exactly 25 tickers must be ACCEPTED and
/// rendered, not rejected. Guards the off-by-one risk (`> 25` silently becoming
/// `>= 25`), which the 26-ticker test cannot catch. Oracle derived from the
/// documented cap before reading the body.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsGetLatestClosingPricesMaxTickerBoundaryTests : ParadeDbMcpTestBase
{
    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            new Equibles.CorporateActions.Repositories.StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    public StockPriceToolsGetLatestClosingPricesMaxTickerBoundaryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLatestClosingPrices_ExactlyMaxTickers_IsAcceptedNotRejected()
    {
        // 25 distinct tickers is the documented maximum, so it must pass the cap and
        // render the table (unseeded tickers produce "Not found" rows), never the
        // over-limit message reserved for 26+.
        var tickers = string.Join(",", Enumerable.Range(1, 25).Select(i => $"T{i:D3}"));

        var result = await Sut().GetLatestClosingPrices(tickers);

        result.Should().NotContain("Maximum 25 tickers per request");
        result
            .Should()
            .Contain(
                "| Ticker | Date | Close | Change | Change % | Volume | 52W High | 52W Low | Off High | Above Low |"
            );
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.CommonStocks;

/// <summary>
/// <see cref="CommonStockRepository.GetByTicker"/>'s second OR branch —
/// <c>cs.SecondaryTickers.Contains(ticker)</c> — translates to
/// <c>= ANY(SecondaryTickers)</c> against Postgres' <c>text[]</c> column, but the EF Core
/// in-memory provider cannot translate <see cref="List{T}.Contains"/> on a navigation
/// scalar list at all (the existing <see cref="Equibles.IntegrationTests.Integrations.TickerMapServiceTests"/>
/// has to register a client-evaluating repository to work around this). Every MCP tool
/// that resolves a ticker — FailToDeliverTools, StockPriceTools, ShortDataTools,
/// InstitutionalHoldingsTools, etc. — funnels through this method, so a regression in
/// the array translation would silently drop secondary-ticker lookups (e.g., a query
/// for the old FB symbol no longer resolving to META) without any test in the unit or
/// in-memory integration tiers catching it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CommonStockRepositorySecondaryTickerTests : ParadeDbMcpTestBase
{
    public CommonStockRepositorySecondaryTickerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetByTicker_QueryMatchesSecondaryTicker_ReturnsStockWithMatchingSecondaryEntry()
    {
        // META renamed from FB in 2022 — production keeps "FB" as a secondary ticker so
        // historical queries against FB still resolve to the current META row. If
        // Postgres array-Contains translation regresses, this lookup returns null and
        // every MCP tool call against FB silently reports "Stock not found".
        var meta = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "META",
            Name = "Meta Platforms Inc.",
            SecondaryTickers = ["FB"],
        };
        // Distractor row with no overlapping tickers — ensures the WHERE clause filters
        // correctly rather than just returning the first row in the table.
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            SecondaryTickers = [],
        };

        DbContext.Set<CommonStock>().AddRange(meta, apple);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var sut = new CommonStockRepository(DbContext);

        var result = await sut.GetByTicker("FB");

        result
            .Should()
            .NotBeNull(
                "the secondary-ticker branch of the WHERE clause must match the FB → META mapping"
            );
        result!
            .Ticker.Should()
            .Be(
                "META",
                "GetByTicker returns the row whose primary OR secondary list contains the query"
            );
    }
}

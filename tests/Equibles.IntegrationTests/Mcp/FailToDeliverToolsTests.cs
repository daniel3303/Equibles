using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FailToDeliverToolsTests : ParadeDbMcpTestBase
{
    private FailToDeliverTools Sut() =>
        new(
            new FailToDeliverRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FailToDeliverTools>()
        );

    public FailToDeliverToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static CommonStock GmeStock() =>
        new()
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };

    // ── GetFailsToDeliver ────────────────────────────────────────────────

    [Fact]
    public async Task GetFailsToDeliver_UnknownTicker_ReturnsStockNotFoundMessage()
    {
        var result = await Sut().GetFailsToDeliver("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetFailsToDeliver_StockWithoutFtds_ReturnsEmptyRangeMessage()
    {
        DbContext.Set<CommonStock>().Add(GmeStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFailsToDeliver("GME", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("No FTD data found for GME");
    }

    [Fact]
    public async Task GetFailsToDeliver_RendersTableAscendingWithValue()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<FailToDeliver>()
            .AddRange(
                new FailToDeliver
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 4, 1),
                    Quantity = 100_000,
                    Price = 25.50m,
                },
                new FailToDeliver
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 4, 2),
                    Quantity = 200_000,
                    Price = 26.00m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFailsToDeliver("GME", startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("Fails-to-deliver for GME (GameStop Corp)");
        result.Should().Contain("2026-04-01");
        result.Should().Contain("2026-04-02");
        result.Should().Contain("100,000");
        result.Should().Contain("$25.50");
        // value = 100_000 × 25.50 = $2,550,000
        result.Should().Contain("$2,550,000");
        result.IndexOf("2026-04-01").Should().BeLessThan(result.IndexOf("2026-04-02"));
    }

    [Fact]
    public async Task GetFailsToDeliver_DateRangeExcludesOutsideRows()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<FailToDeliver>()
            .AddRange(
                new FailToDeliver
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 1, 15),
                    Quantity = 99_999,
                    Price = 1m,
                },
                new FailToDeliver
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 4, 15),
                    Quantity = 200_000,
                    Price = 1m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFailsToDeliver("GME", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("200,000");
        result.Should().NotContain("99,999");
    }

    [Fact]
    public async Task GetFailsToDeliver_MaxResultsLimitsRows()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        var ftds = Enumerable
            .Range(1, 5)
            .Select(i => new FailToDeliver
            {
                CommonStock = stock,
                CommonStockId = stock.Id,
                SettlementDate = new DateOnly(2026, 4, i),
                Quantity = 10_000 * i,
                Price = 25.00m,
            });
        DbContext.Set<FailToDeliver>().AddRange(ftds);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFailsToDeliver(
                "GME",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                maxResults: 2
            );

        // Newest two retained then re-rendered ascending → days 4 and 5.
        result.Should().Contain("2026-04-04");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }

    [Fact]
    public async Task GetFailsToDeliver_TrimsAndUppercasesTicker()
    {
        DbContext.Set<CommonStock>().Add(GmeStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFailsToDeliver("  gme  ");

        result.Should().NotContain("not found");
    }
}

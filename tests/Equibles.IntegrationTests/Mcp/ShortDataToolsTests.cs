using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static CommonStock GmeStock() =>
        new()
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };

    private static CommonStock AmcStock() =>
        new()
        {
            Ticker = "AMC",
            Name = "AMC Entertainment",
            Cik = "0001411579",
        };

    // ── GetShortVolume ───────────────────────────────────────────────────

    [Fact]
    public async Task GetShortVolume_UnknownTicker_ReturnsStockNotFoundMessage()
    {
        // ShortDataTools must short-circuit when the ticker doesn't match any stock —
        // otherwise the subsequent GetHistoryByStock(null) call dereferences the missing
        // entity and the tool returns the generic McpToolExecutor error, masking the real
        // user-facing "stock not found" feedback. Pin the early-return path.
        var result = await Sut().GetShortVolume("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetShortVolume_StockWithoutData_ReturnsEmptyRangeMessage()
    {
        DbContext.Set<CommonStock>().Add(GmeStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().Contain("No short volume data found for GME");
    }

    [Fact]
    public async Task GetShortVolume_StockWithData_RendersAscendingTable()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<DailyShortVolume>()
            .AddRange(
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 4, 1),
                    ShortVolume = 1_000_000,
                    ShortExemptVolume = 50_000,
                    TotalVolume = 2_500_000,
                    Market = "ALL",
                },
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 4, 2),
                    ShortVolume = 1_500_000,
                    ShortExemptVolume = 75_000,
                    TotalVolume = 3_000_000,
                    Market = "ALL",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("Daily short volume for GME (GameStop Corp)");
        result.Should().Contain("2026-04-01");
        result.Should().Contain("2026-04-02");
        result.Should().Contain("1,000,000");
        // ShortVolume / TotalVolume = 1,000,000 / 2,500,000 = 0.40 → "40.0%"
        result.Should().Contain("40.0%");
        // Ascending render order in the body of the table.
        result.IndexOf("2026-04-01").Should().BeLessThan(result.IndexOf("2026-04-02"));
    }

    [Fact]
    public async Task GetShortVolume_ComputesShortPercentageCorrectly()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<DailyShortVolume>()
            .Add(
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 4, 1),
                    ShortVolume = 750_000,
                    ShortExemptVolume = 25_000,
                    TotalVolume = 1_000_000,
                    Market = "ALL",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-03-01", endDate: "2026-04-30");

        result.Should().Contain("75.0%");
    }

    [Fact]
    public async Task GetShortVolume_MaxResultsLimitsRows()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        var days = Enumerable
            .Range(1, 5)
            .Select(i => new DailyShortVolume
            {
                CommonStock = stock,
                CommonStockId = stock.Id,
                Date = new DateOnly(2026, 4, i),
                ShortVolume = 1_000_000,
                ShortExemptVolume = 0,
                TotalVolume = 2_000_000,
                Market = "ALL",
            });
        DbContext.Set<DailyShortVolume>().AddRange(days);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-04-01", endDate: "2026-04-30", maxResults: 2);

        // Newest two retained, then re-ordered ascending in render.
        result.Should().Contain("2026-04-04");
        result.Should().Contain("2026-04-05");
        result.Should().NotContain("2026-04-03");
    }

    [Fact]
    public async Task GetShortVolume_TrimsAndUppercasesTicker()
    {
        DbContext.Set<CommonStock>().Add(GmeStock());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortVolume("  gme  ");

        result.Should().NotContain("not found");
    }

    // ── GetShortInterest ─────────────────────────────────────────────────

    [Fact]
    public async Task GetShortInterest_UnknownTicker_ReturnsStockNotFoundMessage()
    {
        var result = await Sut().GetShortInterest("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetShortInterest_StockWithData_RendersTable()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 50_000_000,
                    PreviousShortPosition = 45_000_000,
                    ChangeInShortPosition = 5_000_000,
                    AverageDailyVolume = 10_000_000,
                    DaysToCover = 5.0m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortInterest("GME", startDate: "2026-01-01", endDate: "2026-04-30");

        result.Should().Contain("Short interest for GME (GameStop Corp)");
        result.Should().Contain("2026-03-15");
        result.Should().Contain("50,000,000");
        result.Should().Contain("+5,000,000"); // Positive sign rendered explicitly.
        result.Should().Contain("5.0");
    }

    [Fact]
    public async Task GetShortInterest_NegativeChange_RendersWithoutPlusSign()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 40_000_000,
                    PreviousShortPosition = 45_000_000,
                    ChangeInShortPosition = -5_000_000,
                    DaysToCover = 4.0m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortInterest("GME", startDate: "2026-01-01", endDate: "2026-04-30");

        result.Should().Contain("-5,000,000");
        result.Should().NotContain("+-5"); // Make sure we didn't double-sign.
    }

    [Fact]
    public async Task GetShortInterest_NullDaysToCover_RendersEmDash()
    {
        var stock = GmeStock();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 1_000,
                    ChangeInShortPosition = 0,
                    DaysToCover = null,
                    AverageDailyVolume = null,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterest("GME");

        result.Should().Contain("| — | — |");
    }

    // ── GetShortInterestSnapshot ─────────────────────────────────────────

    [Fact]
    public async Task GetShortInterestSnapshot_NoData_ReturnsEmptyMessage()
    {
        var result = await Sut().GetShortInterestSnapshot();

        result.Should().Be("No short interest data available.");
    }

    [Fact]
    public async Task GetShortInterestSnapshot_RanksByDaysToCoverDescending()
    {
        var gme = GmeStock();
        var amc = AmcStock();
        DbContext.Set<CommonStock>().AddRange(gme, amc);
        var snapshotDate = new DateOnly(2026, 3, 15);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStock = gme,
                    CommonStockId = gme.Id,
                    SettlementDate = snapshotDate,
                    CurrentShortPosition = 50_000_000,
                    ChangeInShortPosition = 1_000_000,
                    AverageDailyVolume = 5_000_000,
                    DaysToCover = 8.0m,
                },
                new ShortInterest
                {
                    CommonStock = amc,
                    CommonStockId = amc.Id,
                    SettlementDate = snapshotDate,
                    CurrentShortPosition = 30_000_000,
                    ChangeInShortPosition = 500_000,
                    AverageDailyVolume = 10_000_000,
                    DaysToCover = 3.0m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot();

        result.Should().Contain($"settlement date {snapshotDate:yyyy-MM-dd}");
        result.IndexOf("GME").Should().BeLessThan(result.IndexOf("AMC"));
    }

    [Fact]
    public async Task GetShortInterestSnapshot_MinDaysToCoverFiltersResults()
    {
        var gme = GmeStock();
        var amc = AmcStock();
        DbContext.Set<CommonStock>().AddRange(gme, amc);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStock = gme,
                    CommonStockId = gme.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 1_000,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 100_000,
                    DaysToCover = 8.0m,
                },
                new ShortInterest
                {
                    CommonStock = amc,
                    CommonStockId = amc.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 1_000,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 200_000,
                    DaysToCover = 2.0m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot(minDaysToCover: 5.0m);

        result.Should().Contain("GME");
        result.Should().NotContain("AMC");
    }

    [Fact]
    public async Task GetShortInterestSnapshot_ExcludesZeroVolumeStocks()
    {
        var gme = GmeStock();
        var pennyStock = new CommonStock
        {
            Ticker = "BLNC",
            Name = "Penny Corp",
            Cik = "0009999999",
        };
        DbContext.Set<CommonStock>().AddRange(gme, pennyStock);
        var snapshotDate = new DateOnly(2026, 3, 15);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStock = gme,
                    CommonStockId = gme.Id,
                    SettlementDate = snapshotDate,
                    CurrentShortPosition = 50_000_000,
                    ChangeInShortPosition = 1_000_000,
                    AverageDailyVolume = 5_000_000,
                    DaysToCover = 8.0m,
                },
                new ShortInterest
                {
                    CommonStock = pennyStock,
                    CommonStockId = pennyStock.Id,
                    SettlementDate = snapshotDate,
                    CurrentShortPosition = 1,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 0,
                    DaysToCover = 1000.0m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot();

        result.Should().Contain("GME");
        result.Should().NotContain("BLNC");
    }

    [Fact]
    public async Task GetShortInterestSnapshot_UsesOnlyLatestSettlementDate()
    {
        var gme = GmeStock();
        DbContext.Set<CommonStock>().Add(gme);
        DbContext
            .Set<ShortInterest>()
            .AddRange(
                new ShortInterest
                {
                    CommonStock = gme,
                    CommonStockId = gme.Id,
                    SettlementDate = new DateOnly(2026, 2, 28),
                    CurrentShortPosition = 99_999,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 5_000_000,
                    DaysToCover = 7.0m,
                },
                new ShortInterest
                {
                    CommonStock = gme,
                    CommonStockId = gme.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 100_000,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 5_000_000,
                    DaysToCover = 8.0m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot();

        result.Should().Contain("2026-03-15");
        result.Should().Contain("100,000");
        result.Should().NotContain("99,999");
        result.Should().NotContain("2026-02-28");
    }
}

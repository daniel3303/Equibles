using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Cover for the short-data history tools' output notes: the truncation signpost when
/// maxResults cuts rows inside the requested range (silently keeping the NEWEST rows while
/// rendering oldest-first previously read as "the range starts here"), the coverage-edge
/// message when the requested range predates the dataset floor, and the split-boundary
/// Change reconciliation in GetShortInterest.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsHistoryOutputNotesTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            new ShortSqueezeScoreManager(
                new ShortInterestRepository(DbContext),
                new DailyShortVolumeRepository(DbContext),
                new CommonStockRepository(DbContext),
                new StockSplitRepository(DbContext),
                new FailToDeliverRepository(DbContext),
                new DailyStockPriceRepository(DbContext),
                []
            ),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsHistoryOutputNotesTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CommonStock AddGme()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private void AddVolume(CommonStock stock, DateOnly date) =>
        DbContext
            .Set<DailyShortVolume>()
            .Add(
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = date,
                    ShortVolume = 1_000_000,
                    ShortExemptVolume = 0,
                    TotalVolume = 2_000_000,
                    Market = "ALL",
                }
            );

    private void AddShortInterest(
        CommonStock stock,
        DateOnly settlementDate,
        long position,
        long previous,
        long change
    ) =>
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = settlementDate,
                    CurrentShortPosition = position,
                    PreviousShortPosition = previous,
                    ChangeInShortPosition = change,
                    AverageDailyVolume = 1_000_000,
                    DaysToCover = 2.0m,
                }
            );

    [Fact]
    public async Task GetShortVolume_TruncatedRange_AppendsNewestKeptNote()
    {
        var stock = AddGme();
        for (var day = 1; day <= 5; day++)
            AddVolume(stock, new DateOnly(2026, 4, day));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-04-01", endDate: "2026-04-30", maxResults: 2);

        // "first 2 of 5" would misread — the kept rows are the NEWEST, displayed oldest-first.
        result.Should().Contain("Showing the newest 2 of 5 trading days");
    }

    [Fact]
    public async Task GetShortVolume_CompleteRange_HasNoTruncationNote()
    {
        var stock = AddGme();
        AddVolume(stock, new DateOnly(2026, 4, 1));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().NotContain("Showing the newest");
    }

    [Fact]
    public async Task GetShortVolume_RangeBeforeCoverage_NamesTheCoverageFloor()
    {
        // Full-universe FINRA daily files were only loaded from a fixed floor; a range
        // before it must say so instead of the generic (false-reading) "no data for GME".
        var stock = AddGme();
        AddVolume(stock, new DateOnly(2026, 4, 6));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-01-05", endDate: "2026-01-09");

        result.Should().Contain("coverage starts on 2026-04-06");
    }

    [Fact]
    public async Task GetShortVolume_EmptyRangeInsideCoverage_KeepsGenericMessage()
    {
        var stock = AddGme();
        AddVolume(stock, new DateOnly(2026, 4, 6));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortVolume("GME", startDate: "2026-04-11", endDate: "2026-04-12");

        result.Should().Contain("No short volume data found for GME");
        result.Should().NotContain("coverage starts");
    }

    [Fact]
    public async Task GetShortInterest_TruncatedRange_AppendsNewestKeptNote()
    {
        var stock = AddGme();
        AddShortInterest(stock, new DateOnly(2026, 2, 13), 100, 90, 10);
        AddShortInterest(stock, new DateOnly(2026, 2, 27), 110, 100, 10);
        AddShortInterest(stock, new DateOnly(2026, 3, 13), 120, 110, 10);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortInterest("GME", startDate: "2026-01-01", endDate: "2026-03-31", maxResults: 2);

        result.Should().Contain("Showing the newest 2 of 3 settlements");
        result.Should().Contain("2026-02-27");
        result.Should().NotContain("| 2026-02-13");
    }

    [Fact]
    public async Task GetShortInterest_SplitStraddlingSettlement_ChangeReconcilesAcrossRows()
    {
        // A 10:1 split between the two settlements: FINRA's raw change (=current − previous)
        // mixes a pre-split previous position with a post-split current one. The displayed
        // Change must equal the difference of the two DISPLAYED (restated) positions —
        // 5,500,000 − 5,000,000 = +500,000 — not the raw 5,500,000 − 500,000 = +5,000,000.
        var stock = AddGme();
        AddShortInterest(
            stock,
            new DateOnly(2026, 2, 27),
            position: 500_000,
            previous: 450_000,
            change: 50_000
        );
        AddShortInterest(
            stock,
            new DateOnly(2026, 3, 13),
            position: 5_500_000,
            previous: 500_000,
            change: 5_000_000
        );
        DbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    EffectiveDate = new DateOnly(2026, 3, 1),
                    Numerator = 10m,
                    Denominator = 1m,
                    Source = StockSplitSource.Manual,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetShortInterest("GME", startDate: "2026-01-01", endDate: "2026-03-31");

        // Pre-split settlement restated onto today's basis.
        result.Should().Contain("| 5,000,000 |");
        // The straddle row's Change reconciles with the adjacent restated positions.
        result.Should().Contain("+500,000");
        result.Should().NotContain("+5,000,000");
    }

    [Fact]
    public async Task GetShortInterest_CappedDaysToCover_RendersSentinel()
    {
        var stock = AddGme();
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 13),
                    CurrentShortPosition = 1_000,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 1,
                    DaysToCover = 999.99m,
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterest("GME");

        result.Should().Contain(">=999.99 (FINRA cap)");
        result.Should().NotContain("1000.0");
    }
}

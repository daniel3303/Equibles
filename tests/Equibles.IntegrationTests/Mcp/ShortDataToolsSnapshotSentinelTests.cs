using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
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
/// Cover for GetShortInterestSnapshot's handling of FINRA's 999.99 days-to-cover cap. The
/// cap is a sentinel ("999.99 or more"), not a reading: in prod ~180 illiquid OTC rows tie
/// at it, so a plain days-to-cover-descending sort made the entire default response capped
/// noise (rendered as a fictitious "1000.0", in nondeterministic order). Contract: capped
/// rows render as the explicit sentinel, rank AFTER real readings, ties break
/// deterministically, and minAvgDailyVolume can drop illiquid names outright.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsSnapshotSentinelTests : ParadeDbMcpTestBase
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

    public ShortDataToolsSnapshotSentinelTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static readonly DateOnly Settlement = new(2026, 6, 30);

    private int _nextCik = 1;

    private CommonStock AddStock(string ticker, string name)
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = name,
            Cik = (_nextCik++).ToString("D10"),
        };
        DbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private void AddShortInterest(
        CommonStock stock,
        decimal daysToCover,
        long position = 1_000_000,
        long avgDailyVolume = 100_000,
        long change = 0
    ) =>
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = Settlement,
                    CurrentShortPosition = position,
                    ChangeInShortPosition = change,
                    AverageDailyVolume = avgDailyVolume,
                    DaysToCover = daysToCover,
                }
            );

    [Fact]
    public async Task Snapshot_CappedDaysToCover_RendersSentinelNotRoundedThousand()
    {
        var illiquid = AddStock("CODQL", "Compagnie OTC");
        AddShortInterest(illiquid, daysToCover: 999.99m, avgDailyVolume: 4);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot();

        // "F1" would round FINRA's 999.99 cap to a fictitious "1000.0" that exceeds the
        // source's own maximum and hides that the value is a sentinel.
        result.Should().Contain(">=999.99 (FINRA cap)");
        result.Should().NotContain("1000.0");
    }

    [Fact]
    public async Task Snapshot_CappedRows_RankAfterRealReadings()
    {
        var real = AddStock("GME", "GameStop Corp");
        AddShortInterest(real, daysToCover: 8.0m, avgDailyVolume: 5_000_000);
        var capped = AddStock("CODQL", "Compagnie OTC");
        AddShortInterest(capped, daysToCover: 999.99m, avgDailyVolume: 4);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot();

        // The cap sentinel is numerically the maximum but must NOT outrank genuine
        // readings — otherwise the default view is 100% illiquid capped names.
        result.IndexOf("GME").Should().BeLessThan(result.IndexOf("CODQL"));
    }

    [Fact]
    public async Task Snapshot_TiedCappedRows_OrderDeterministicallyByPositionThenTicker()
    {
        // Three rows tied at the cap: the ordering must be reproducible across calls
        // (position descending, then ticker) instead of Postgres' arbitrary tie order.
        AddShortInterest(AddStock("BBB", "Bravo Corp"), 999.99m, position: 500, avgDailyVolume: 2);
        AddShortInterest(AddStock("AAA", "Alpha Corp"), 999.99m, position: 500, avgDailyVolume: 2);
        AddShortInterest(AddStock("CCC", "Chase Corp"), 999.99m, position: 900, avgDailyVolume: 2);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot();

        // CCC leads on the larger position; AAA/BBB tie on position and break on ticker.
        result.IndexOf("CCC").Should().BeLessThan(result.IndexOf("AAA"));
        result.IndexOf("AAA").Should().BeLessThan(result.IndexOf("BBB"));
    }

    [Fact]
    public async Task Snapshot_MinAvgDailyVolume_DropsIlliquidNames()
    {
        var liquid = AddStock("GME", "GameStop Corp");
        AddShortInterest(liquid, daysToCover: 8.0m, avgDailyVolume: 5_000_000);
        var illiquid = AddStock("CODQL", "Compagnie OTC");
        AddShortInterest(illiquid, daysToCover: 999.99m, avgDailyVolume: 4);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot(minAvgDailyVolume: 100_000);

        result.Should().Contain("GME");
        result.Should().NotContain("CODQL");
    }

    [Fact]
    public async Task Snapshot_SortByShortPosition_RanksByPosition()
    {
        var small = AddStock("GME", "GameStop Corp");
        AddShortInterest(small, daysToCover: 9.0m, position: 1_000, avgDailyVolume: 100);
        var big = AddStock("AMC", "AMC Entertainment");
        AddShortInterest(big, daysToCover: 1.0m, position: 90_000_000, avgDailyVolume: 90_000_000);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot(sortBy: "shortPosition");

        // Under the position sort the big position leads even with the lower days-to-cover.
        result.IndexOf("AMC").Should().BeLessThan(result.IndexOf("GME"));
    }

    [Fact]
    public async Task Snapshot_TruncatedResults_AppendTruncationNote()
    {
        AddShortInterest(AddStock("AAA", "Alpha Corp"), 5.0m);
        AddShortInterest(AddStock("BBB", "Bravo Corp"), 4.0m);
        AddShortInterest(AddStock("CCC", "Chase Corp"), 3.0m);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot(maxResults: 2);

        result.Should().Contain("Showing first 2 of 3 results");
    }

    [Fact]
    public async Task Snapshot_CompleteResults_HaveNoTruncationNote()
    {
        AddShortInterest(AddStock("AAA", "Alpha Corp"), 5.0m);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetShortInterestSnapshot();

        result.Should().NotContain("Showing first");
    }
}

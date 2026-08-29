using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepository13FAvailableReportDatesTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepository13FAvailableReportDatesTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new CorporateActionsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: the market-wide report-date list must be 13F-only and newest-first.
    // Schedule 13D/G rows carry a daily event date, not a quarter end; if a later 13D/G
    // date leaked in, callers that treat index 0 as "latest" and index 1 as "prior
    // quarter" would compare a quarter-end portfolio against the prior DAY — the regression
    // behind the market-wide activity boards (double-down) showing zero positions.
    [Fact]
    public async Task Get13FAvailableReportDates_ExcludesLater13DGEventDates_NewestFirst()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Holder A",
        };

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var q3 = new DateOnly(2024, 9, 30);
        // A 13D/G stake filed AFTER the latest 13F quarter — exactly the row that
        // pollutes the all-filings list and makes "prior" the prior day.
        var event13G = new DateOnly(2024, 11, 14);

        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, holder.Id, q2, FilingType.Form13F, "13F-Q2"),
                Holding(stock.Id, holder.Id, q1, FilingType.Form13F, "13F-Q1"),
                Holding(stock.Id, holder.Id, q3, FilingType.Form13F, "13F-Q3"),
                Holding(stock.Id, holder.Id, event13G, FilingType.Schedule13G, "13G-EVENT"),
                Holding(stock.Id, holder.Id, event13G, FilingType.Schedule13D, "13D-EVENT")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository
            .Get13FAvailableReportDates()
            .ToListAsync(CancellationToken.None);

        // Only the 13F quarter ends, newest-first; the 2024-11-14 event date is gone, so
        // index 0 is the latest quarter and index 1 is the genuine prior quarter.
        dates.Should().Equal(q3, q2, q1);
    }

    [Fact]
    public async Task Get13FAvailableReportDatesCached_UsesSnapshotSpineAndPrependsRefreshLag()
    {
        var stock = Stock("GLOBAL");
        var holder = Holder("0000000010");
        var snapshotted = new DateOnly(2024, 6, 30);
        var live = new DateOnly(2024, 9, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<AumQuarterlySnapshot>()
            .Add(new AumQuarterlySnapshot { ReportDate = snapshotted, FilerCount = 1 });
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, live, FilingType.Form13F, "13F-LIVE"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FAvailableReportDatesCached();

        dates.Should().Equal(live, snapshotted);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_ReturnsSnapshotDatesNewestFirst()
    {
        var stock = Stock("MSFT");
        var holder = Holder("0000000005");
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .AddRange(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = q1,
                    CurrentFilerCount = 1,
                },
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = q2,
                    CurrentFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, q2, FilingType.Form13F, "13F-Q2"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(q2, q1);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_PrependsNewestLiveQuarterDuringRefreshLag()
    {
        var stock = Stock("NVDA");
        var holder = Holder("0000000002");
        var snapshotQuarter = new DateOnly(2024, 6, 30);
        var liveQuarter = new DateOnly(2024, 9, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .Add(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = snapshotQuarter,
                    CurrentFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, liveQuarter, FilingType.Form13F, "13F-LIVE"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(liveQuarter, snapshotQuarter);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_ExcludesSoldOutSnapshotQuarter()
    {
        var stock = Stock("EXIT");
        var holder = Holder("0000000004");
        var heldQuarter = new DateOnly(2024, 3, 31);
        var soldOutQuarter = new DateOnly(2024, 6, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .AddRange(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = heldQuarter,
                    CurrentFilerCount = 1,
                },
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = soldOutQuarter,
                    CurrentFilerCount = 0,
                    PreviousFilerCount = 1,
                    SoldOutFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, heldQuarter, FilingType.Form13F, "13F-HELD"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(heldQuarter);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_DropsSnapshotNewerThanLiveHistory()
    {
        var stock = Stock("STALE");
        var holder = Holder("0000000006");
        var q1 = new DateOnly(2024, 3, 31);
        var liveQuarter = new DateOnly(2024, 6, 30);
        var staleSnapshotQuarter = new DateOnly(2024, 9, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .AddRange(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = q1,
                    CurrentFilerCount = 1,
                },
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = staleSnapshotQuarter,
                    CurrentFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, liveQuarter, FilingType.Form13F, "13F-LIVE-Q2"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(liveQuarter, q1);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_ReturnsEmptyWhenSnapshotHasNoLiveHoldings()
    {
        var stock = Stock("EMPTY");
        _dbContext.Add(stock);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .Add(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = new DateOnly(2024, 9, 30),
                    CurrentFilerCount = 1,
                }
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().BeEmpty();
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_FallsBackToLive13FHistoryWithoutSnapshot()
    {
        var stock = Stock("MU");
        var holder = Holder("0000000003");
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var schedule13GDate = new DateOnly(2024, 8, 12);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, holder.Id, q1, FilingType.Form13F, "13F-Q1"),
                Holding(stock.Id, holder.Id, q2, FilingType.Form13F, "13F-Q2"),
                Holding(stock.Id, holder.Id, schedule13GDate, FilingType.Schedule13G, "13G")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(q2, q1);
    }

    [Fact]
    public async Task Get13FReportDatesByHolderSnapshotBacked_UsesOnlyThatHolderAndPrependsLive()
    {
        var stock = Stock("HOLDER");
        var holder = Holder("0000000011");
        var other = Holder("0000000012");
        var snapshotted = new DateOnly(2024, 6, 30);
        var live = new DateOnly(2024, 9, 30);

        _dbContext.AddRange(stock, holder, other);
        _dbContext
            .Set<HolderQuarterlySnapshot>()
            .AddRange(
                Snapshot(holder.Id, snapshotted),
                Snapshot(other.Id, new DateOnly(2024, 12, 31))
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, live, FilingType.Form13F, "13F-HOLDER-LIVE"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByHolderSnapshotBacked(holder);

        dates.Should().Equal(live, snapshotted);
    }

    [Fact]
    public async Task Get13FReportDatesByHolderSnapshotBacked_FallsBackWithoutSnapshot()
    {
        var stock = Stock("FALLBACK");
        var holder = Holder("0000000013");
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, holder.Id, q1, FilingType.Form13F, "13F-H-Q1"),
                Holding(stock.Id, holder.Id, q2, FilingType.Form13F, "13F-H-Q2")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByHolderSnapshotBacked(holder);

        dates.Should().Equal(q2, q1);
    }

    [Fact]
    public async Task GetStockActivitySnapshotsByStockSnapshotBacked_FallsBackToStockScoped13F()
    {
        var stock = Stock("TREND");
        var first = Holder("0000000014");
        var second = Holder("0000000015");
        var quarter = new DateOnly(2024, 6, 30);
        _dbContext.AddRange(stock, first, second);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, first.Id, quarter, FilingType.Form13F, "13F-A"),
                Holding(stock.Id, second.Id, quarter, FilingType.Form13F, "13F-B"),
                Holding(stock.Id, second.Id, quarter, FilingType.Schedule13G, "13G-B")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var rows = await _repository.GetStockActivitySnapshotsByStockSnapshotBacked(stock);

        var row = rows.Should().ContainSingle().Which;
        row.ReportDate.Should().Be(quarter);
        row.CurrentShares.Should().Be(200);
        row.CurrentValue.Should().Be(2000);
        row.CurrentFilerCount.Should().Be(2);
    }

    [Fact]
    public async Task GetStockActivitySnapshotsByStockSnapshotBacked_MergesImplicitAndExplicitPrimaryListing()
    {
        var stock = Stock("LBRDK");
        var first = Holder("0000000020");
        var second = Holder("0000000021");
        var quarter = new DateOnly(2026, 3, 31);
        var implicitPrimary = Holding(
            stock.Id,
            first.Id,
            quarter,
            FilingType.Form13F,
            "13F-IMPLICIT-PRIMARY"
        );
        var explicitPrimary = Holding(
            stock.Id,
            second.Id,
            quarter,
            FilingType.Form13F,
            "13F-EXPLICIT-PRIMARY"
        );
        explicitPrimary.ListedTicker = stock.Ticker;
        _dbContext.AddRange(stock, first, second, implicitPrimary, explicitPrimary);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var rows = await _repository.GetStockActivitySnapshotsByStockSnapshotBacked(stock);

        var activity = rows.Should().ContainSingle().Which;
        var listing = activity.ListingShares.Should().ContainSingle().Which;
        listing.PriceSeriesTicker.Should().Be(stock.Ticker);
        listing.CurrentShares.Should().Be(200);
    }

    [Fact]
    public async Task GetStockActivitySnapshotsByStockSnapshotBacked_BoundsStaleRowsAndAppendsRefreshLag()
    {
        var stock = Stock("TREND-LAG");
        var first = Holder("0000000018");
        var second = Holder("0000000019");
        var older = new DateOnly(2024, 3, 31);
        var snapshotted = new DateOnly(2024, 6, 30);
        var latest = new DateOnly(2024, 9, 30);
        var staleFuture = new DateOnly(2024, 12, 31);
        var computedAt = DateTime.UtcNow;
        _dbContext.AddRange(stock, first, second);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .AddRange(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = snapshotted,
                    CurrentShares = 100,
                    CurrentValue = 1000,
                    CurrentFilerCount = 1,
                    ComputedAt = computedAt,
                },
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = staleFuture,
                    CurrentShares = 999,
                    CurrentValue = 9999,
                    CurrentFilerCount = 9,
                    ComputedAt = computedAt,
                }
            );
        _dbContext
            .Set<StockQuarterlyListingActivity>()
            .Add(
                new StockQuarterlyListingActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = snapshotted,
                    PriceSeriesTicker = stock.Ticker,
                    CurrentShares = 100,
                    ComputedAt = computedAt,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, first.Id, older, FilingType.Form13F, "13F-LAG-OLD"),
                Holding(stock.Id, first.Id, snapshotted, FilingType.Form13F, "13F-LAG-PRIOR"),
                Holding(stock.Id, first.Id, latest, FilingType.Form13F, "13F-LAG-A"),
                Holding(stock.Id, second.Id, latest, FilingType.Form13F, "13F-LAG-B")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var rows = await _repository.GetStockActivitySnapshotsByStockSnapshotBacked(stock);

        rows.Select(row => row.ReportDate).Should().Equal(snapshotted, latest);
        var newest = rows[^1];
        newest.CurrentShares.Should().Be(200);
        newest.PreviousReportDate.Should().Be(snapshotted);
        newest.PreviousShares.Should().Be(100);
        newest.CurrentValue.Should().Be(2000);
        newest.PreviousValue.Should().Be(1000);
        newest.CurrentFilerCount.Should().Be(2);
        newest.PreviousFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetCombinedStockActivitySnapshotBacked_LoadsOneVersionedListingGeneration()
    {
        var stock = Stock("TREND-COMBINED");
        var previous = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var computedAt = DateTime.UtcNow;
        _dbContext.Add(stock);
        _dbContext.Add(
            new StockQuarterlyActivityCombined
            {
                CommonStockId = stock.Id,
                ReportDate = current,
                PreviousReportDate = previous,
                CurrentShares = 2_500,
                PreviousShares = 2_100,
                CurrentValue = 250_000,
                PreviousValue = 210_000,
                CurrentFilerCount = 8,
                PreviousFilerCount = 7,
                ComputedAt = computedAt,
            }
        );
        _dbContext
            .Set<StockQuarterlyListingActivity>()
            .AddRange(
                new StockQuarterlyListingActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = current,
                    IsCombined = true,
                    PriceSeriesTicker = stock.Ticker,
                    CurrentShares = 1_000,
                    PreviousShares = 900,
                    ComputedAt = computedAt,
                },
                new StockQuarterlyListingActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = current,
                    IsCombined = true,
                    PriceSeriesTicker = "TREND-COMBINED.B",
                    CurrentShares = 1_500,
                    PreviousShares = 1_200,
                    ComputedAt = computedAt,
                }
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var row = await _repository.GetCombinedStockActivitySnapshotBacked(
            stock,
            current,
            previous
        );

        row.Should().NotBeNull();
        row.CurrentShares.Should().Be(2_500);
        row.CurrentValue.Should().Be(250_000);
        row.CurrentFilerCount.Should().Be(8);
        row.ListingShares.Select(listing => listing.PriceSeriesTicker)
            .Should()
            .BeEquivalentTo(stock.Ticker, "TREND-COMBINED.B");
    }

    [Fact]
    public async Task GetHolderQuarterlySnapshotsSnapshotBacked_FallsBackOnlyForMissingHolder()
    {
        var stock = Stock("FUNDS");
        var snapshotted = Holder("0000000016");
        var missing = Holder("0000000017");
        var quarter = new DateOnly(2024, 6, 30);
        _dbContext.AddRange(stock, snapshotted, missing);
        _dbContext
            .Set<HolderQuarterlySnapshot>()
            .Add(
                new HolderQuarterlySnapshot
                {
                    InstitutionalHolderId = snapshotted.Id,
                    ReportDate = quarter,
                    FilingDate = quarter.AddDays(45),
                    Aum = 123,
                    PositionCount = 1,
                    StockCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, snapshotted.Id, quarter, FilingType.Form13F, "13F-SNAPSHOT"),
                Holding(stock.Id, missing.Id, quarter, FilingType.Form13F, "13F-MISSING"),
                Holding(stock.Id, missing.Id, quarter, FilingType.Schedule13D, "13D-MISSING")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var rows = await _repository.GetHolderQuarterlySnapshotsSnapshotBacked([
            snapshotted.Id,
            missing.Id,
        ]);

        rows.Should().HaveCount(2);
        rows.Single(row => row.InstitutionalHolderId == snapshotted.Id).Aum.Should().Be(123);
        var fallback = rows.Single(row => row.InstitutionalHolderId == missing.Id);
        fallback.Aum.Should().Be(1000);
        fallback.PositionCount.Should().Be(1);
        fallback.StockCount.Should().Be(1);
    }

    private static CommonStock Stock(string ticker) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = $"{ticker} Inc.",
            Cik = Guid.NewGuid().ToString("N")[..10],
        };

    private static InstitutionalHolder Holder(string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Cik = cik,
            Name = $"Holder {cik}",
        };

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        FilingType filingType,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate,
            FilingType = filingType,
            Shares = 100,
            Value = 1000,
            AccessionNumber = accession,
        };

    private static HolderQuarterlySnapshot Snapshot(Guid holderId, DateOnly reportDate) =>
        new()
        {
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Aum = 1000,
            PositionCount = 1,
            StockCount = 1,
        };
}

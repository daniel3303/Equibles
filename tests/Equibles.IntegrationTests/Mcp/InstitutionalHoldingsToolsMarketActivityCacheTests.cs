using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsMarketActivityCacheTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsMarketActivityCacheTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetMarketWide13FActivity_RebuildVersionInvalidatesCachedQuarter()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var holder = new InstitutionalHolder { Cik = "H1", Name = "Test Filer" };
        DbContext.AddRange(stock, holder);
        var computedAt = DateTime.UtcNow;
        DbContext.AddRange(
            MakeHolding(stock, holder, prior, shares: 100, value: 100_000),
            MakeHolding(stock, holder, current, shares: 200, value: 200_000),
            new StockQuarterlyActivity
            {
                CommonStockId = stock.Id,
                ReportDate = current,
                PreviousReportDate = prior,
                CurrentShares = 200,
                PreviousShares = 100,
                CurrentValue = 200_000,
                PreviousValue = 100_000,
                CurrentFilerCount = 1,
                PreviousFilerCount = 1,
                ComputedAt = computedAt,
            },
            new StockQuarterlyListingActivity
            {
                CommonStockId = stock.Id,
                ReportDate = current,
                PriceSeriesTicker = stock.Ticker,
                CurrentShares = 200,
                PreviousShares = 100,
                ComputedAt = computedAt,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var firstRead = Fixture.CreateDbContext();
        var sut = BuildTool(firstRead, cache);
        var first = await sut.GetMarketWide13FActivity(
            bucket: "top-buys",
            reportDate: "2024-12-31"
        );

        await using (var update = Fixture.CreateDbContext())
        {
            var snapshot = await update.Set<StockQuarterlyActivity>().FindAsync(stock.Id, current);
            snapshot.CurrentShares = 350;
            snapshot.ComputedAt = snapshot.ComputedAt.AddMinutes(1);
            var listing = await update.Set<StockQuarterlyListingActivity>().SingleAsync();
            listing.CurrentShares = snapshot.CurrentShares;
            listing.ComputedAt = snapshot.ComputedAt;
            await update.SaveChangesAsync();
        }

        var refreshed = await sut.GetMarketWide13FActivity(
            bucket: "top-buys",
            reportDate: "2024-12-31"
        );
        refreshed.Should().NotBe(first);

        using var freshCache = new MemoryCache(new MemoryCacheOptions());
        await using var freshRead = Fixture.CreateDbContext();
        var independentlyRead = await BuildTool(freshRead, freshCache)
            .GetMarketWide13FActivity(bucket: "top-buys", reportDate: "2024-12-31");
        independentlyRead.Should().Be(refreshed);
    }

    [Fact]
    public async Task GetMarketWide13FActivity_CachedQuarterRefiltersDeactivatedStock()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var stock = new CommonStock
        {
            Ticker = "GONE",
            Name = "Formerly Listed Inc.",
            Cik = "C-DELISTED",
        };
        var holder = new InstitutionalHolder { Cik = "H-DELISTED", Name = "Test Filer" };
        DbContext.AddRange(stock, holder);
        var computedAt = DateTime.UtcNow;
        DbContext.AddRange(
            MakeHolding(stock, holder, prior, shares: 100, value: 100_000),
            MakeHolding(stock, holder, current, shares: 200, value: 200_000),
            new StockQuarterlyActivity
            {
                CommonStockId = stock.Id,
                ReportDate = current,
                PreviousReportDate = prior,
                CurrentShares = 200,
                PreviousShares = 100,
                CurrentValue = 200_000,
                PreviousValue = 100_000,
                CurrentFilerCount = 1,
                PreviousFilerCount = 1,
                ComputedAt = computedAt,
            },
            new StockQuarterlyListingActivity
            {
                CommonStockId = stock.Id,
                ReportDate = current,
                PriceSeriesTicker = stock.Ticker,
                CurrentShares = 200,
                PreviousShares = 100,
                ComputedAt = computedAt,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var read = Fixture.CreateDbContext();
        var sut = BuildTool(read, cache);
        var active = await sut.GetMarketWide13FActivity(
            bucket: "top-buys",
            reportDate: "2024-12-31"
        );
        active.Should().Contain("GONE");

        await using (var update = Fixture.CreateDbContext())
        {
            var deactivated = await update
                .Set<CommonStock>()
                .SingleAsync(row => row.Id == stock.Id);
            deactivated.Active = false;
            await update.SaveChangesAsync();
        }

        var inactive = await sut.GetMarketWide13FActivity(
            bucket: "top-buys",
            reportDate: "2024-12-31"
        );
        inactive.Should().NotContain("GONE");
        inactive.Should().NotContain("Unknown");
    }

    [Fact]
    public async Task GetMarketWide13FActivity_DirtyQuarterReadsSnapshotWithoutCachingIt()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var stock = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "C2",
        };
        var holder = new InstitutionalHolder { Cik = "H2", Name = "Dirty Filer" };
        DbContext.AddRange(stock, holder);
        var computedAt = DateTime.UtcNow;
        DbContext.AddRange(
            MakeHolding(stock, holder, prior, shares: 100, value: 100_000),
            MakeHolding(stock, holder, current, shares: 200, value: 200_000),
            new StockQuarterlyActivity
            {
                CommonStockId = stock.Id,
                ReportDate = current,
                PreviousReportDate = prior,
                CurrentShares = 200,
                PreviousShares = 100,
                CurrentValue = 200_000,
                PreviousValue = 100_000,
                CurrentFilerCount = 1,
                PreviousFilerCount = 1,
                ComputedAt = computedAt,
            },
            new StockQuarterlyListingActivity
            {
                CommonStockId = stock.Id,
                ReportDate = current,
                PriceSeriesTicker = stock.Ticker,
                CurrentShares = 200,
                PreviousShares = 100,
                ComputedAt = computedAt,
            },
            new AumQuarterlySnapshot { ReportDate = prior, FilerCount = 1 },
            new AumQuarterlySnapshot
            {
                ReportDate = current,
                FilerCount = 1,
                DirtyAt = DateTime.UtcNow,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = BuildTool(DbContext, cache);
        var first = await sut.GetMarketWide13FActivity(
            bucket: "top-buys",
            reportDate: "2024-12-31"
        );

        var currentHolding = await DbContext
            .Set<InstitutionalHolding>()
            .SingleAsync(row => row.ReportDate == current);
        currentHolding.Shares = 900;
        currentHolding.Value = 900_000;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var afterLiveMutation = await sut.GetMarketWide13FActivity(
            bucket: "top-buys",
            reportDate: "2024-12-31"
        );
        afterLiveMutation.Should().Be(first, "dirty requests must not run the live aggregate");

        var snapshot = await DbContext
            .Set<StockQuarterlyActivity>()
            .SingleAsync(row => row.ReportDate == current);
        snapshot.CurrentShares = 350;
        snapshot.CurrentValue = 350_000;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var afterSnapshotMutation = await sut.GetMarketWide13FActivity(
            bucket: "top-buys",
            reportDate: "2024-12-31"
        );
        afterSnapshotMutation.Should().NotBe(first, "dirty snapshots are never process-cached");
    }

    private InstitutionalHoldingsTools BuildTool(
        EquiblesFinancialDbContext context,
        IMemoryCache cache
    )
    {
        var holdingRepository = new InstitutionalHoldingRepository(context);
        var stockSplitRepository = new StockSplitRepository(context);
        return new InstitutionalHoldingsTools(
            holdingRepository,
            new InstitutionalHolderRepository(context),
            new CommonStockRepository(context),
            stockSplitRepository,
            new StockCombinedQuarterService(holdingRepository, stockSplitRepository),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>(),
            memoryCache: cache
        );
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{stock.Ticker}-{reportDate:yyyyMMdd}",
        };
}

using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionPortfolioSummaryProviderCacheTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public InstitutionPortfolioSummaryProviderCacheTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new CorporateActionsModuleConfiguration()
        );
    }

    public void Dispose()
    {
        _cache.Dispose();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task Get_DirtyIngestBypassesCachedSummaryUntilSnapshotRefresh()
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
            Name = "Test Holder",
        };
        var previous = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var currentHolding = Holding(stock.Id, holder.Id, current, 100, "CURRENT");

        _dbContext.AddRange(stock, holder, currentHolding);
        _dbContext.Add(Holding(stock.Id, holder.Id, previous, 50, "PREVIOUS"));
        _dbContext.AddRange(
            Snapshot(holder.Id, previous, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Snapshot(holder.Id, current, new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            new AumQuarterlySnapshot { ReportDate = previous },
            new AumQuarterlySnapshot { ReportDate = current }
        );
        await _dbContext.SaveChangesAsync();

        var provider = new InstitutionPortfolioSummaryProvider(
            new InstitutionalHoldingRepository(_dbContext),
            _cache
        );
        var first = await provider.Get(holder, current, previous, quartersReported: 2);
        first.ReportedAum.Should().Be(100);

        currentHolding.Value = 200;
        _dbContext.Set<AumQuarterlySnapshot>().Find(current).DirtyAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var duringDirtyWindow = await provider.Get(holder, current, previous, quartersReported: 2);
        duringDirtyWindow.ReportedAum.Should().Be(200);

        currentHolding.Value = 300;
        await _dbContext.SaveChangesAsync();
        var secondDirtyRead = await provider.Get(holder, current, previous, quartersReported: 2);
        secondDirtyRead.ReportedAum.Should().Be(300);
    }

    [Fact]
    public async Task Get_CleanSnapshotVersionInvalidatesCachedSummary()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Versioned Holder",
        };
        var previous = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var currentHolding = Holding(stock.Id, holder.Id, current, 100, "CURRENT-VERSIONED");
        var currentSnapshot = Snapshot(
            holder.Id,
            current,
            new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        );

        _dbContext.AddRange(stock, holder, currentHolding);
        _dbContext.Add(Holding(stock.Id, holder.Id, previous, 50, "PREVIOUS-VERSIONED"));
        _dbContext.AddRange(
            Snapshot(holder.Id, previous, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            currentSnapshot,
            new AumQuarterlySnapshot { ReportDate = previous },
            new AumQuarterlySnapshot { ReportDate = current }
        );
        await _dbContext.SaveChangesAsync();

        var provider = new InstitutionPortfolioSummaryProvider(
            new InstitutionalHoldingRepository(_dbContext),
            _cache
        );
        var first = await provider.Get(holder, current, previous, quartersReported: 2);
        first.ReportedAum.Should().Be(100);

        currentHolding.Value = 200;
        await _dbContext.SaveChangesAsync();

        var cached = await provider.Get(holder, current, previous, quartersReported: 2);
        cached.ReportedAum.Should().Be(100);

        currentSnapshot.ComputedAt = currentSnapshot.ComputedAt.AddMinutes(1);
        await _dbContext.SaveChangesAsync();

        var refreshed = await provider.Get(holder, current, previous, quartersReported: 2);
        refreshed.ReportedAum.Should().Be(200);
    }

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long value,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            FilingType = FilingType.Form13F,
            Shares = value,
            Value = value,
            AccessionNumber = accession,
        };

    private static HolderQuarterlySnapshot Snapshot(
        Guid holderId,
        DateOnly reportDate,
        DateTime computedAt
    ) =>
        new()
        {
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Aum = 100,
            PositionCount = 1,
            StockCount = 1,
            ComputedAt = computedAt,
        };
}

using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// Pins the distinct, capped exact-series selection for pending splits and cash dividends.
/// </summary>
public class CorporateActionPriceReconciliationManagerSelectionTests
{
    private static readonly DateOnly SettledBefore = new(2026, 8, 10);

    private static CorporateActionPriceReconciliationManager NewManager(
        EquiblesFinancialDbContext db
    ) =>
        new(
            new StockSplitRepository(db),
            new CashDividendRepository(db),
            new CommonStockRepository(db),
            new CorporateActionPriceReconciliationCursorRepository(db)
        );

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warning => warning.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
        context.Database.EnsureCreated();
        return context;
    }

    private static CommonStock Stock(
        Guid id,
        string ticker = "AAPL",
        List<string> secondaryTickers = null
    ) =>
        new()
        {
            Id = id,
            Ticker = ticker,
            SecondaryTickers = secondaryTickers ?? [],
        };

    private static StockSplit PendingSplit(
        Guid stockId,
        DateOnly effectiveDate,
        string listedTicker = "AAPL"
    ) =>
        new()
        {
            CommonStockId = stockId,
            PriceSeriesTicker = listedTicker,
            EffectiveDate = effectiveDate,
            Numerator = 2m,
            Denominator = 1m,
            Source = StockSplitSource.Yahoo,
        };

    private static CashDividend PendingDividend(
        Guid stockId,
        DateOnly exDate,
        decimal amount = 0.25m
    ) =>
        new()
        {
            CommonStockId = stockId,
            ExDate = exDate,
            AmountPerShare = amount,
            Source = CashDividendSource.Yahoo,
        };

    [Fact]
    public async Task SelectPendingSeries_SplitAndDividendForPrimary_CollapseToOneSeries()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2021, 1, 4)),
            PendingSplit(stockId, new DateOnly(2024, 6, 10)),
            PendingDividend(stockId, new DateOnly(2024, 2, 9)),
            PendingDividend(stockId, new DateOnly(2024, 5, 9))
        );
        await db.SaveChangesAsync();

        var selection = await NewManager(db).SelectPendingSeries(50, SettledBefore);

        var selected = selection.Series.Should().ContainSingle().Which;
        selected.ListedTicker.Should().Be("AAPL");
        selected.Splits.Should().HaveCount(2);
        selected.Dividends.Should().HaveCount(2);
        selection.TotalPending.Should().Be(1);
        selection.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task SelectPendingSeries_CapsDistinctSeriesAndReportsRemainder()
    {
        await using var db = NewDb();
        for (var index = 0; index < 5; index++)
        {
            var stockId = Guid.NewGuid();
            db.Add(Stock(stockId, $"T{index}"));
            db.Add(PendingDividend(stockId, new DateOnly(2024, 1, 1)));
        }
        await db.SaveChangesAsync();

        var selection = await NewManager(db).SelectPendingSeries(2, SettledBefore);

        selection.Series.Should().HaveCount(2);
        selection.TotalPending.Should().Be(5);
        selection.Skipped.Should().Be(3);
    }

    [Fact]
    public async Task SelectPendingSeries_CappedFailuresRotateInsteadOfStarvingLaterSeries()
    {
        await using var db = NewDb();
        for (var index = 0; index < 3; index++)
        {
            var stockId = Guid.NewGuid();
            db.Add(Stock(stockId, $"T{index}"));
            db.Add(PendingDividend(stockId, new DateOnly(2024, 1, 1)));
        }
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var first = (await manager.SelectPendingSeries(1, SettledBefore)).Series.Single();
        var second = (await manager.SelectPendingSeries(1, SettledBefore)).Series.Single();
        var third = (await manager.SelectPendingSeries(1, SettledBefore)).Series.Single();
        var wrapped = (await manager.SelectPendingSeries(1, SettledBefore)).Series.Single();

        new[] { first.CommonStockId, second.CommonStockId, third.CommonStockId }
            .Should()
            .OnlyHaveUniqueItems();
        wrapped.CommonStockId.Should().Be(first.CommonStockId);
    }

    [Fact]
    public async Task SelectPendingSeries_FutureActionsRemainPendingThroughEffectiveDate()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 8, 12);
        db.Add(Stock(stockId));
        db.AddRange(PendingSplit(stockId, effectiveDate), PendingDividend(stockId, effectiveDate));
        await db.SaveChangesAsync();

        var manager = NewManager(db);

        (await manager.SelectPendingSeries(50, effectiveDate)).Series.Should().BeEmpty();
        var eligible = await manager.SelectPendingSeries(50, effectiveDate.AddDays(1));

        var series = eligible.Series.Should().ContainSingle().Which;
        series.Splits.Should().ContainSingle();
        series.Dividends.Should().ContainSingle();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public async Task SelectPendingSeries_PrematurelyStampedSplit_RequeuesAfterEffectiveDate(
        int appliedDay
    )
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 8, 12);
        db.Add(Stock(stockId));
        var split = PendingSplit(stockId, effectiveDate);
        split.PriceAdjustmentAppliedTime = new DateTime(
            2026,
            8,
            appliedDay,
            12,
            0,
            0,
            DateTimeKind.Utc
        );
        db.Add(split);
        await db.SaveChangesAsync();

        var manager = NewManager(db);

        (await manager.SelectPendingSeries(50, effectiveDate)).Series.Should().BeEmpty();
        var selected = (await manager.SelectPendingSeries(50, effectiveDate.AddDays(1)))
            .Series.Should()
            .ContainSingle()
            .Which;

        var correctedAppliedTime = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
        (await manager.StampApplied(selected, correctedAppliedTime)).Should().Be(1);
        split.PriceAdjustmentAppliedTime.Should().Be(correctedAppliedTime);
        (await manager.SelectPendingSeries(50, effectiveDate.AddDays(1))).Series.Should().BeEmpty();
    }

    [Fact]
    public void SplitPendingPredicate_TranslatesForNpgsql()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        using var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );

        var sql = new StockSplitRepository(db).GetPendingPriceAdjustment().ToQueryString();

        sql.Should().Contain("AT TIME ZONE 'UTC' AS date");
        sql.Should().Contain("\"EffectiveDate\"");
    }

    [Fact]
    public async Task SelectPendingSeries_IgnoresActionsWhoseAppliedSnapshotStillMatches()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        var split = PendingSplit(stockId, new DateOnly(2024, 6, 10));
        split.PriceAdjustmentAppliedTime = DateTime.UtcNow;
        var dividend = PendingDividend(stockId, new DateOnly(2024, 5, 9));
        dividend.PriceAdjustmentAppliedAmountPerShare = dividend.AmountPerShare;
        dividend.PriceAdjustmentAppliedTime = DateTime.UtcNow;
        db.AddRange(split, dividend);
        await db.SaveChangesAsync();

        var selection = await NewManager(db).SelectPendingSeries(50, SettledBefore);

        selection.Series.Should().BeEmpty();
        selection.TotalPending.Should().Be(0);
    }

    [Fact]
    public async Task SelectPendingSeries_KeepsSplitSeriesIndependentAndTargetsDividendAtPrimary()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId, "GOOGL", ["GOOG"]));
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2024, 1, 1), "GOOGL"),
            PendingSplit(stockId, new DateOnly(2024, 2, 1), "GOOG"),
            PendingSplit(stockId, new DateOnly(2024, 3, 1), listedTicker: null),
            PendingDividend(stockId, new DateOnly(2024, 4, 1))
        );
        await db.SaveChangesAsync();

        var selection = await NewManager(db).SelectPendingSeries(50, SettledBefore);

        selection.Series.Select(series => series.ListedTicker).Should().Equal("GOOG", "GOOGL");
        selection
            .Series.Single(series => series.ListedTicker == "GOOG")
            .Dividends.Should()
            .BeEmpty();
        selection
            .Series.Single(series => series.ListedTicker == "GOOGL")
            .Dividends.Should()
            .ContainSingle();
    }

    [Fact]
    public async Task SelectPendingSeries_RetiringWorkerRestatesAmount_SelectsDividendAgain()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        var dividend = PendingDividend(stockId, new DateOnly(2024, 5, 9), 0.26m);
        dividend.PriceAdjustmentAppliedAmountPerShare = 0.24m;
        dividend.PriceAdjustmentAppliedTime = DateTime.UtcNow;
        db.Add(dividend);
        await db.SaveChangesAsync();

        var selection = await NewManager(db).SelectPendingSeries(50, SettledBefore);

        selection
            .Series.Should()
            .ContainSingle()
            .Which.Dividends.Should()
            .ContainSingle(snapshot => snapshot.AmountPerShare == 0.26m);
    }
}

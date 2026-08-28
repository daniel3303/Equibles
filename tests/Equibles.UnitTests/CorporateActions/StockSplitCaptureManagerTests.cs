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

public class StockSplitCaptureManagerTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

    private static StockSplitCaptureManager NewManager(EquiblesFinancialDbContext context) =>
        new(new StockSplitRepository(context), new CommonStockRepository(context));

    private static CapturedSplit Split(decimal numerator = 2m) =>
        new()
        {
            EffectiveDate = new DateOnly(2024, 2, 1),
            Numerator = numerator,
            Denominator = 1m,
            Source = StockSplitSource.Yahoo,
        };

    [Fact]
    public async Task Capture_StalePrimaryTargetAfterReorder_DoesNotWriteIssuerAction()
    {
        await using var context = NewDb();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "GOOG",
            SecondaryTickers = ["GOOGL"],
        };
        context.Add(stock);
        await context.SaveChangesAsync();

        // The crawl originally saw GOOGL as primary. By the write boundary it is secondary, so
        // only the exact price series may be updated; the issuer-level action must be skipped.
        var staleWrite = await NewManager(context).Capture(stock.Id, "GOOGL", [Split()]);

        staleWrite.Should().Be(0);
        (await context.Set<StockSplit>().ToListAsync()).Should().BeEmpty();

        var currentWrite = await NewManager(context).Capture(stock.Id, "GOOG", [Split()]);

        currentWrite.Should().Be(1);
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.PriceSeriesTicker.Should().Be("GOOG");
    }

    [Fact]
    public async Task Capture_SameDateAlreadyAttributedToSibling_PreservesOriginalSeriesAndRatio()
    {
        await using var context = NewDb();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "GOOG",
            SecondaryTickers = ["GOOGL"],
        };
        context.Add(stock);
        context.Add(
            new StockSplit
            {
                CommonStockId = stock.Id,
                PriceSeriesTicker = "GOOGL",
                EffectiveDate = new DateOnly(2024, 2, 1),
                Numerator = 2m,
                Denominator = 1m,
                Source = StockSplitSource.Yahoo,
            }
        );
        await context.SaveChangesAsync();

        var changes = await NewManager(context).Capture(stock.Id, "GOOG", [Split(20m)]);

        changes.Should().Be(0);
        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.PriceSeriesTicker.Should().Be("GOOGL");
        stored.Numerator.Should().Be(2m);
    }

    [Fact]
    public async Task Capture_UnattributedLegacyRow_AttributesOnlyFromExactCurrentPrimaryObservation()
    {
        await using var context = NewDb();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "GOOG",
            SecondaryTickers = ["GOOGL"],
        };
        context.Add(stock);
        context.Add(
            new StockSplit
            {
                CommonStockId = stock.Id,
                PriceSeriesTicker = null,
                EffectiveDate = new DateOnly(2024, 2, 1),
                Numerator = 2m,
                Denominator = 1m,
                Source = StockSplitSource.Yahoo,
                PriceAdjustmentAppliedTime = DateTime.UtcNow,
            }
        );
        await context.SaveChangesAsync();

        var secondaryObservation = await NewManager(context)
            .Capture(stock.Id, "GOOGL", [Split(20m)]);

        secondaryObservation.Should().Be(0);
        context.ChangeTracker.Clear();
        var stillUnattributed = await context.Set<StockSplit>().SingleAsync();
        stillUnattributed.PriceSeriesTicker.Should().BeNull();
        stillUnattributed.Numerator.Should().Be(2m);
        stillUnattributed.PriceAdjustmentAppliedTime.Should().NotBeNull();

        var primaryObservation = await NewManager(context).Capture(stock.Id, "GOOG", [Split(20m)]);

        primaryObservation.Should().Be(1);
        context.ChangeTracker.Clear();
        var attributed = await context.Set<StockSplit>().SingleAsync();
        attributed.PriceSeriesTicker.Should().Be("GOOG");
        attributed.Numerator.Should().Be(20m);
        attributed.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public async Task Capture_NonPositiveRatioArm_DoesNotPersist(decimal numerator, decimal denominator)
    {
        await using var context = NewDb();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "SAFE",
        };
        context.Add(stock);
        await context.SaveChangesAsync();
        var split = Split(numerator);
        split.Denominator = denominator;

        var changes = await NewManager(context).Capture(stock.Id, stock.Ticker, [split]);

        changes.Should().Be(0);
        (await context.Set<StockSplit>().ToListAsync()).Should().BeEmpty();
    }
}

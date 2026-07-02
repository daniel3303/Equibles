using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// Pins the contract of <see cref="StockSplitBackfillManager"/>: one full-range chart request
/// covering [since, today], the returned split events upserted through the existing idempotent
/// capture path (stamped as Yahoo-sourced, with a null PriceAdjustmentAppliedTime so the price
/// sync's reconciliation re-adjusts the stored series), the written count returned — and a
/// re-run over already-captured history writes nothing.
/// </summary>
public class StockSplitBackfillManagerTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static StockSplitBackfillManager NewManager(
        EquiblesFinancialDbContext db,
        IYahooFinanceClient yahooClient
    ) => new(yahooClient, new StockSplitCaptureManager(new StockSplitRepository(db)));

    private static IYahooFinanceClient ClientReturning(params StockSplitEvent[] splits)
    {
        var client = Substitute.For<IYahooFinanceClient>();
        client
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData { Splits = splits.ToList() });
        return client;
    }

    [Fact]
    public async Task BackfillHistory_RequestsOneChartCoveringSinceThroughToday()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var since = new DateOnly(2020, 1, 1);
        var client = ClientReturning();

        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        await NewManager(db, client).BackfillHistory(stock, since, CancellationToken.None);
        var after = DateOnly.FromDateTime(DateTime.UtcNow);

        // The end bound is "today" (UTC); tolerate a run that crosses UTC midnight.
        await client
            .Received(1)
            .GetChart("AAPL", since, Arg.Is<DateOnly>(d => d >= before && d <= after));
    }

    [Fact]
    public async Task BackfillHistory_UpsertsReturnedSplitsAsYahooSourcedAndPendingReconciliation()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var client = ClientReturning(
            new StockSplitEvent
            {
                Date = new DateOnly(2020, 8, 31),
                Numerator = 4m,
                Denominator = 1m,
            }
        );

        var captured = await NewManager(db, client)
            .BackfillHistory(stock, new DateOnly(2020, 1, 1), CancellationToken.None);

        captured.Should().Be(1);
        var stored = await new StockSplitRepository(db).GetByStock(stock.Id).ToListAsync();
        var split = stored.Should().ContainSingle().Which;
        split.EffectiveDate.Should().Be(new DateOnly(2020, 8, 31));
        split.Numerator.Should().Be(4m);
        split.Denominator.Should().Be(1m);
        split.Source.Should().Be(StockSplitSource.Yahoo);
        // Null marks the stock pending for the price sync's split reconciliation,
        // which re-pulls the fully-adjusted history — the backfill itself never
        // touches stored prices.
        split.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task BackfillHistory_Rerun_IsIdempotentAndWritesNothing()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var client = ClientReturning(
            new StockSplitEvent
            {
                Date = new DateOnly(2020, 8, 31),
                Numerator = 4m,
                Denominator = 1m,
            }
        );
        var since = new DateOnly(2020, 1, 1);

        await NewManager(db, client).BackfillHistory(stock, since, CancellationToken.None);
        var secondPass = await NewManager(db, client)
            .BackfillHistory(stock, since, CancellationToken.None);

        secondPass.Should().Be(0);
        (await new StockSplitRepository(db).GetByStock(stock.Id).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task BackfillHistory_NoSplitsInWindow_WritesNothingAndReturnsZero()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };

        var captured = await NewManager(db, ClientReturning())
            .BackfillHistory(stock, new DateOnly(2020, 1, 1), CancellationToken.None);

        captured.Should().Be(0);
        (await new StockSplitRepository(db).GetByStock(stock.Id).AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task BackfillHistory_AlreadyCancelled_ThrowsWithoutFetching()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var client = ClientReturning();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
            await NewManager(db, client)
                .BackfillHistory(stock, new DateOnly(2020, 1, 1), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await client
            .DidNotReceive()
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }
}

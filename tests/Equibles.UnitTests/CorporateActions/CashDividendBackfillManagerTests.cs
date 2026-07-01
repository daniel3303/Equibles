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
/// Pins the contract of <see cref="CashDividendBackfillManager"/>: one full-range chart request
/// covering [since, today], the returned dividends upserted through the existing idempotent
/// capture path (stamped as Yahoo-sourced), the written count returned — and a re-run over
/// already-captured history writes nothing.
/// </summary>
public class CashDividendBackfillManagerTests
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

    private static CashDividendBackfillManager NewManager(
        EquiblesFinancialDbContext db,
        IYahooFinanceClient yahooClient
    ) => new(yahooClient, new CashDividendCaptureManager(new CashDividendRepository(db)));

    private static IYahooFinanceClient ClientReturning(params CashDividendEvent[] dividends)
    {
        var client = Substitute.For<IYahooFinanceClient>();
        client
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData { Dividends = dividends.ToList() });
        return client;
    }

    [Fact]
    public async Task BackfillHistory_RequestsOneChartCoveringSinceThroughToday()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-730);
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
    public async Task BackfillHistory_UpsertsReturnedDividendsAsYahooSourced()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var client = ClientReturning(
            new CashDividendEvent { Date = new DateOnly(2024, 2, 9), Amount = 0.24m },
            new CashDividendEvent { Date = new DateOnly(2024, 5, 9), Amount = 0.25m }
        );

        var captured = await NewManager(db, client)
            .BackfillHistory(
                stock,
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-730),
                CancellationToken.None
            );

        captured.Should().Be(2);
        var stored = await new CashDividendRepository(db).GetByStock(stock.Id).ToListAsync();
        stored.Should().HaveCount(2);
        stored.Single(d => d.ExDate == new DateOnly(2024, 2, 9)).AmountPerShare.Should().Be(0.24m);
        stored.Should().OnlyContain(d => d.Source == CashDividendSource.Yahoo);
    }

    [Fact]
    public async Task BackfillHistory_Rerun_IsIdempotentAndWritesNothing()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var client = ClientReturning(
            new CashDividendEvent { Date = new DateOnly(2024, 2, 9), Amount = 0.24m }
        );
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-730);

        await NewManager(db, client).BackfillHistory(stock, since, CancellationToken.None);
        var secondPass = await NewManager(db, client)
            .BackfillHistory(stock, since, CancellationToken.None);

        secondPass.Should().Be(0);
        (await new CashDividendRepository(db).GetByStock(stock.Id).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task BackfillHistory_NoDividendsInWindow_WritesNothingAndReturnsZero()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };

        var captured = await NewManager(db, ClientReturning())
            .BackfillHistory(
                stock,
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-730),
                CancellationToken.None
            );

        captured.Should().Be(0);
        (await new CashDividendRepository(db).GetByStock(stock.Id).AnyAsync()).Should().BeFalse();
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
                .BackfillHistory(
                    stock,
                    DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-730),
                    cts.Token
                );

        await act.Should().ThrowAsync<OperationCanceledException>();
        await client
            .DidNotReceive()
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }
}

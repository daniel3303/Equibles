using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

[Collection(ParadeDbCollection.Name)]
public class YahooPriceImportServiceConcurrentWriterTests : ParadeDbMcpTestBase
{
    private static readonly MethodInfo FlushPriceBatchMethod =
        typeof(YahooPriceImportService).GetMethod(
            "FlushPriceBatch",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;

    public YahooPriceImportServiceConcurrentWriterTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task FlushPriceBatch_ConcurrentDateAlreadyCommitted_SkipsItAndCommitsRemainingRows()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Cik = "0000000991",
            Ticker = "RACE",
            Name = "Concurrent Writer Test",
        };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var collisionDate = new DateOnly(2026, 8, 20);
        var remainingDate = collisionDate.AddDays(1);
        var staleBatch = new List<DailyStockPrice>
        {
            Price(stock.Id, collisionDate, 99m),
            Price(stock.Id, remainingDate, 101m),
        };

        await using var flushContext = Fixture.CreateDbContext();
        await using var concurrentWriter = Fixture.CreateDbContext();
        await using var writerTransaction = await concurrentWriter.Database.BeginTransactionAsync();
        var writerStockRepo = new CommonStockRepository(concurrentWriter);
        var writerPriceRepo = new DailyStockPriceRepository(concurrentWriter);
        await writerStockRepo.GetForUpdate(stock.Id);
        writerPriceRepo.Add(Price(stock.Id, collisionDate, 100m));
        await writerPriceRepo.SaveChanges();
        var writerPid = ((NpgsqlConnection)concurrentWriter.Database.GetDbConnection()).ProcessID;

        var service = BuildService(flushContext);
        var flushTask = (Task)FlushPriceBatchMethod.Invoke(service, [staleBatch])!;
        await WaitUntilBlockedBy(writerPid);
        await writerTransaction.CommitAsync();
        await flushTask;

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify
            .Set<DailyStockPrice>()
            .AsNoTracking()
            .Where(price => price.CommonStockId == stock.Id && price.ListedTicker == stock.Ticker)
            .OrderBy(price => price.Date)
            .ToListAsync();
        stored.Select(price => price.Date).Should().Equal(collisionDate, remainingDate);
        stored.Select(price => price.Close).Should().Equal(100m, 101m);
    }

    private YahooPriceImportService BuildService(EquiblesFinancialDbContext context)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(context)),
            (typeof(DailyStockPriceRepository), new DailyStockPriceRepository(context))
        );
        return new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            Substitute.For<IYahooFinanceClient>(),
            new TickerMapService(scopeFactory),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions())
        );
    }

    private async Task WaitUntilBlockedBy(int writerPid)
    {
        await using var observer = Fixture.CreateDbContext();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var blocked = await observer
                .Database.SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM pg_stat_activity
                    WHERE {writerPid} = ANY(pg_blocking_pids(pid))
                    """
                )
                .SingleAsync();
            if (blocked > 0)
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException("Yahoo flush did not block on the concurrent writer's lock.");
    }

    private static DailyStockPrice Price(Guid stockId, DateOnly date, decimal close) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            ListedTicker = "RACE",
            Date = date,
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            AdjustedClose = close,
            Volume = 1_000,
        };
}

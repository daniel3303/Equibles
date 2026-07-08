using System.Collections;
using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// SeedCusips historically only filled stocks whose CUSIP was still null, so an
/// issuer-level CUSIP change (BBUC's Class A conversion retired 11259V106 for
/// 113006100 in Q1 2026) was never picked up: every new 13F line referenced a
/// CUSIP nothing mapped, and the stock's holder count silently collapsed to the
/// laggard filers still using the old CUSIP. Pin the change-detection contract:
/// (1) a changed FTD CUSIP updates the stored stock, (2) the retired CUSIP is
/// recorded as a <see cref="CommonStockCusipAlias"/> so old filings keep
/// resolving, (3) StockCusipChanged is published so Holdings backfills, and
/// (4) the per-symbol CUSIP is resolved by LATEST SETTLEMENT DATE — a
/// transition file carries both CUSIPs, and neither first-row-wins nor
/// last-row-wins picks the right one.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdImportServiceSeedCusipsUpdatesChangedCusipTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FtdImportServiceSeedCusipsUpdatesChangedCusipTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    [Fact]
    public async Task SeedCusips_SymbolCusipChanged_UpdatesStockRecordsAliasAndPublishesEvent()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBUC",
            Name = "Brookfield Business Corp",
            Cik = "1654795",
            Cusip = "11259V106",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var publishEndpoint = Substitute.For<IBus>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(new CommonStockRepository(ctx), publishEndpoint)
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        // Transition file: the retiring CUSIP trades on the early settlement
        // days and the replacement on the latest. Order the rows so the
        // newest-dated row sits in the middle — first-row-wins and
        // last-row-wins would both resolve the OLD CUSIP; only
        // latest-settlement-date-wins resolves the NEW one.
        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;

        void AddRecord(string cusip, DateOnly settlementDate)
        {
            var record = Activator.CreateInstance(recordType)!;
            recordType.GetProperty("Cusip")!.SetValue(record, cusip);
            recordType.GetProperty("Symbol")!.SetValue(record, "BBUC");
            recordType.GetProperty("SettlementDate")!.SetValue(record, settlementDate);
            list.Add(record);
        }

        AddRecord("11259V106", new DateOnly(2026, 3, 10));
        AddRecord("113006100", new DateOnly(2026, 3, 27));
        AddRecord("11259V106", new DateOnly(2026, 3, 5));

        var tickerMap = new Dictionary<string, Guid> { ["BBUC"] = stock.Id };

        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedCusips.Invoke(sut, [list, tickerMap, CancellationToken.None])!;

        seeded.Should().Be(1);

        await publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == stock.Id
                    && e.Ticker == "BBUC"
                    && e.Cusip == "113006100"
                    && e.PreviousCusip == "11259V106"
                ),
                Arg.Any<CancellationToken>()
            );

        using var verify = FreshContext();
        var persisted = await verify.Set<CommonStock>().FirstAsync(s => s.Id == stock.Id);
        persisted.Cusip.Should().Be("113006100");

        var alias = await verify.Set<CommonStockCusipAlias>().SingleAsync();
        alias.Cusip.Should().Be("11259V106");
        alias.CommonStockId.Should().Be(stock.Id);
    }

    [Fact]
    public async Task SeedCusips_ResolvedCusipBelongsToAnotherStock_SkipsWithoutUpdating()
    {
        // Ticker-recycling shape: a delisted issuer's stale stock still holds
        // the freed symbol, and the FTD feed now maps that symbol to a CUSIP
        // that already identifies a different tracked stock. Adopting it would
        // leave two stocks sharing one CUSIP, so the row must be skipped.
        var staleStock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TICK",
            Name = "Delisted Corp",
            Cik = "0000000001",
            Cusip = "111111111",
        };
        var currentOwner = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "NEWCO",
            Name = "New Owner Corp",
            Cik = "0000000002",
            Cusip = "222222222",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().AddRange(staleStock, currentOwner);
            await seed.SaveChangesAsync();
        }

        var publishEndpoint = Substitute.For<IBus>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(new CommonStockRepository(ctx), publishEndpoint)
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Cusip")!.SetValue(record, "222222222");
        recordType.GetProperty("Symbol")!.SetValue(record, "TICK");
        recordType.GetProperty("SettlementDate")!.SetValue(record, new DateOnly(2026, 6, 12));
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        list.Add(record);

        var tickerMap = new Dictionary<string, Guid>
        {
            ["TICK"] = staleStock.Id,
            ["NEWCO"] = currentOwner.Id,
        };

        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedCusips.Invoke(sut, [list, tickerMap, CancellationToken.None])!;

        seeded.Should().Be(0);
        await publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());

        using var verify = FreshContext();
        var persistedStale = await verify.Set<CommonStock>().FirstAsync(s => s.Id == staleStock.Id);
        persistedStale.Cusip.Should().Be("111111111");
    }

    [Fact]
    public async Task SeedCusips_CusipUnchanged_UpdatesNothingAndPublishesNothing()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBUC",
            Name = "Brookfield Business Corp",
            Cik = "1654795",
            Cusip = "113006100",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var publishEndpoint = Substitute.For<IBus>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(new CommonStockRepository(ctx), publishEndpoint)
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Cusip")!.SetValue(record, "113006100");
        recordType.GetProperty("Symbol")!.SetValue(record, "BBUC");
        recordType.GetProperty("SettlementDate")!.SetValue(record, new DateOnly(2026, 6, 12));
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        list.Add(record);

        var tickerMap = new Dictionary<string, Guid> { ["BBUC"] = stock.Id };

        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedCusips.Invoke(sut, [list, tickerMap, CancellationToken.None])!;

        seeded.Should().Be(0);
        await publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());

        using var verify = FreshContext();
        (await verify.Set<CommonStockCusipAlias>().AnyAsync()).Should().BeFalse();
    }
}

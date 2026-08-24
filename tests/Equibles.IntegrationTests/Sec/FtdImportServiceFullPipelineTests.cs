using System.IO.Compression;
using System.Net;
using System.Text;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Cftc, Holdings and Cboe have a *ImportServiceFullPipelineTests against the shared
/// ParadeDB fixture; <see cref="FtdImportService"/> is the only remaining HostedService
/// importer without an end-to-end real-DB test. The DB-touching phases —
/// FailToDeliverRepository.GetLatestDate (drives SyncDateResolver), BuildTickerMap
/// (round-trips CommonStock through a real Postgres query), SeedCusips (Postgres-only
/// array translation via CommonStockRepository.GetByTickers + .Where(s.Cusip == null)),
/// the per-batch UpsertRange into FailToDeliver — are not reachable from
/// FtdImportServiceTests's in-memory facts (which only pin GetFileNames and the empty
/// fileNames early-exit). A regression in the import wiring around any of those would
/// silently drop FTD data on every worker tick.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdImportServiceFullPipelineTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FtdImportServiceFullPipelineTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

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

    /// <summary>
    /// IServiceScopeFactory whose every CreateScope() yields a fresh DbContext bound to
    /// the same ParadeDB instance plus the repositories the importer pulls per scope.
    /// TickerMapService is registered with this same factory so its inner CreateScope()
    /// also lands on a fresh context.
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory() => CreateScopeFactory(Substitute.For<IBus>());

    private IServiceScopeFactory CreateScopeFactory(IBus bus, int? failingScope = null)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scopeNumber = 0;
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                scopeNumber++;
                if (scopeNumber == failingScope)
                    throw new InvalidOperationException("Simulated identity-query failure");

                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(new CommonStockManager(new CommonStockRepository(ctx), bus));
                sp.GetService(typeof(FailToDeliverRepository))
                    .Returns(new FailToDeliverRepository(ctx));
                var errorRepository = new ErrorRepository(ctx);
                sp.GetService(typeof(ErrorRepository)).Returns(errorRepository);
                sp.GetService(typeof(ErrorManager)).Returns(new ErrorManager(errorRepository));
                sp.GetService(typeof(TickerMapService)).Returns(new TickerMapService(scopeFactory));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    [Fact]
    public async Task Import_ReplayedTransitionMonth_DoesNotRevertWhenNewerFileTemporarilyFails()
    {
        var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var transitionMonth = currentMonth.AddMonths(-1);
        var olderMonth = transitionMonth.AddMonths(-1);
        var olderDate = olderMonth.AddDays(5);
        var retiringDate = transitionMonth.AddDays(5);
        var replacementDate = transitionMonth.AddDays(20);
        var latestSettledDate = currentMonth.AddDays(-1);
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TAP",
            Name = "Molson Coors Beverage Co",
            Cik = "24545",
            Cusip = "60871R100",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<FailToDeliver>()
                .Add(
                    new FailToDeliver
                    {
                        CommonStockId = stock.Id,
                        SettlementDate = latestSettledDate,
                        Quantity = 777,
                        Price = 52.00m,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var retiringCsv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{retiringDate:yyyyMMdd}|60871R100|TAP|100|MOLSON COORS|50.00\n";
        var replacementCsv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{replacementDate:yyyyMMdd}|60871R209|TAP|200|MOLSON COORS|51.00\n";
        var yearMonth = transitionMonth.ToString("yyyyMM");
        var olderYearMonth = olderMonth.ToString("yyyyMM");
        var olderCsv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{olderDate:yyyyMMdd}|60871R100|TAP|50|MOLSON COORS|49.00\n";
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        var replacementFileAvailable = true;
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns(call =>
            {
                var url = call.Arg<string>();
                if (url.Contains($"cnsfails{olderYearMonth}", StringComparison.Ordinal))
                    return Task.FromResult<Stream>(BuildFtdZipStream(olderCsv));
                if (url.EndsWith($"cnsfails{yearMonth}a.zip", StringComparison.Ordinal))
                    return Task.FromResult<Stream>(BuildFtdZipStream(retiringCsv));
                if (url.EndsWith($"cnsfails{yearMonth}b.zip", StringComparison.Ordinal))
                {
                    return replacementFileAvailable
                        ? Task.FromResult<Stream>(BuildFtdZipStream(replacementCsv))
                        : Task.FromException<Stream>(
                            new HttpRequestException(
                                "Temporary failure",
                                null,
                                HttpStatusCode.ServiceUnavailable
                            )
                        );
                }
                return Task.FromException<Stream>(
                    new HttpRequestException("Not Found", null, HttpStatusCode.NotFound)
                );
            });
        var bus = Substitute.For<IBus>();
        var sut = new FtdImportService(
            CreateScopeFactory(bus),
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = olderMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            )
        );

        await sut.Import(CancellationToken.None);
        await sut.Import(CancellationToken.None);
        replacementFileAvailable = false;
        await sut.Import(CancellationToken.None);

        await bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(change =>
                    change.CommonStockId == stock.Id
                    && change.PreviousCusip == "60871R100"
                    && change.Cusip == "60871R209"
                ),
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1).Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<CommonStock>().SingleAsync()).Cusip.Should().Be("60871R209");
        (await verify.Set<CommonStockCusipAlias>().SingleAsync()).Cusip.Should().Be("60871R100");
        var storedFtd = await verify.Set<FailToDeliver>().SingleAsync();
        storedFtd.SettlementDate.Should().Be(latestSettledDate);
        storedFtd.Quantity.Should().Be(777);
        storedFtd.Price.Should().Be(52.00m);
    }

    [Fact]
    public async Task Import_CurrentFileCancelledAfterCompleteReplay_ReconcilesBeforeCancellation()
    {
        var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var replayMonth = currentMonth.AddMonths(-1);
        var retiringDate = replayMonth.AddDays(5);
        var replacementDate = replayMonth.AddDays(20);
        var latestSettledDate = currentMonth.AddDays(-1);
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TAP",
            Name = "Molson Coors Beverage Co",
            Cik = "24545",
            Cusip = "60871R100",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<FailToDeliver>()
                .Add(
                    new FailToDeliver
                    {
                        CommonStockId = stock.Id,
                        SettlementDate = latestSettledDate,
                        Quantity = 777,
                        Price = 52.00m,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var retiringCsv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{retiringDate:yyyyMMdd}|60871R100|TAP|100|MOLSON COORS|50.00\n";
        var replacementCsv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{replacementDate:yyyyMMdd}|60871R209|TAP|200|MOLSON COORS|51.00\n";
        var replayYearMonth = replayMonth.ToString("yyyyMM");
        var currentYearMonth = currentMonth.ToString("yyyyMM");
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns(call =>
            {
                var url = call.Arg<string>();
                if (url.EndsWith($"cnsfails{replayYearMonth}a.zip", StringComparison.Ordinal))
                    return Task.FromResult<Stream>(BuildFtdZipStream(retiringCsv));
                if (url.EndsWith($"cnsfails{replayYearMonth}b.zip", StringComparison.Ordinal))
                    return Task.FromResult<Stream>(BuildFtdZipStream(replacementCsv));
                if (url.Contains($"cnsfails{currentYearMonth}", StringComparison.Ordinal))
                {
                    return Task.FromException<Stream>(
                        new OperationCanceledException("Current file request cancelled")
                    );
                }
                return Task.FromException<Stream>(
                    new HttpRequestException("Not Found", null, HttpStatusCode.NotFound)
                );
            });
        var bus = Substitute.For<IBus>();
        var sut = new FtdImportService(
            CreateScopeFactory(bus),
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = replayMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            )
        );

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.Import(CancellationToken.None)
        );

        await bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(change =>
                    change.CommonStockId == stock.Id
                    && change.PreviousCusip == "60871R100"
                    && change.Cusip == "60871R209"
                ),
                Arg.Any<CancellationToken>()
            );

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<CommonStock>().SingleAsync()).Cusip.Should().Be("60871R209");
        (await verify.Set<CommonStockCusipAlias>().SingleAsync()).Cusip.Should().Be("60871R100");
        var storedFtd = await verify.Set<FailToDeliver>().SingleAsync();
        storedFtd.SettlementDate.Should().Be(latestSettledDate);
        storedFtd.Quantity.Should().Be(777);
    }

    [Fact]
    public async Task Import_DownloadAndParseAndUpsert_PersistsFailToDeliverAndSeedsCusipOnMatchingStock()
    {
        // AAPL has no Cusip yet — SeedCusips' Postgres-only `GetByTickers(...).Where(s.Cusip == null)`
        // path must lift the FTD-derived CUSIP onto the stock row. If that path regresses,
        // the assertion on apple.Cusip catches it.
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            await seed.SaveChangesAsync();
        }

        // Settlement date ~1 month ago — keeps GetFileNames' iteration bounded as
        // wall-clock time advances. The mock returns the same zip for every file URL,
        // so the per-day grouping in ImportRecords collapses every iteration into the
        // same single row.
        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1).AddDays(-1);
        var csv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{settlementDate:yyyyMMdd}|037833100|AAPL|12345|APPLE INC|187.50\n";

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            // Fresh stream per call — ZipArchive consumes/disposes the input on read.
            .Returns(_ => Task.FromResult<Stream>(BuildFtdZipStream(csv)));

        var sut = new FtdImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            // Pin MinSyncDate to the seeded month so SyncDateResolver starts the walk
            // there even though no FailToDeliver row exists yet (resolver falls back to
            // MinSyncDate when latestDateInDb == default).
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = settlementDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            )
        );

        await sut.Import(CancellationToken.None);

        var replayYearMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1)
            .AddMonths(-1)
            .ToString("yyyyMM");
        await secEdgarClient
            .Received(1)
            .DownloadStream(
                Arg.Is<string>(url =>
                    url.EndsWith($"cnsfails{replayYearMonth}a.zip", StringComparison.Ordinal)
                )
            );
        await secEdgarClient
            .Received(1)
            .DownloadStream(
                Arg.Is<string>(url =>
                    url.EndsWith($"cnsfails{replayYearMonth}b.zip", StringComparison.Ordinal)
                )
            );

        await using var verify = _fixture.CreateDbContext();

        var ftdRow = await verify
            .Set<FailToDeliver>()
            .SingleOrDefaultAsync(f =>
                f.CommonStockId == apple.Id && f.SettlementDate == settlementDate
            );
        ftdRow
            .Should()
            .NotBeNull(
                "the (CommonStockId, SettlementDate) row should be inserted via the UpsertRange INSERT path; "
                    + "absence here means the import dropped the record somewhere between DownloadStream and FlushBatch"
            );
        ftdRow!.Quantity.Should().Be(12345);
        ftdRow.Price.Should().Be(187.50m);

        // SeedCusips path — only reachable when GetByTickers' Postgres array translation
        // resolves the AAPL row AND the Cusip-null filter kicks in.
        var reloadedApple = await verify.Set<CommonStock>().SingleAsync(s => s.Id == apple.Id);
        reloadedApple
            .Cusip.Should()
            .Be(
                "037833100",
                "SeedCusips should lift the FTD-derived CUSIP onto a Cusip-less CommonStock row"
            );
    }

    [Fact]
    public async Task Import_OverlapArchive_PersistsOnlyRecordsAfterDatabaseWatermark()
    {
        var replayMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(
            -1
        );
        var oldDate = replayMonth.AddDays(14);
        var newDate = replayMonth.AddDays(19);
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<FailToDeliver>()
                .Add(
                    new FailToDeliver
                    {
                        CommonStockId = stock.Id,
                        SettlementDate = oldDate,
                        Quantity = 777,
                        Price = 180m,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var csv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{oldDate:yyyyMMdd}|037833100|AAPL|999|APPLE INC|181.00\n"
            + $"{newDate:yyyyMMdd}|037833100|AAPL|123|APPLE INC|182.00\n";
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream>(BuildFtdZipStream(csv)));
        var sut = new FtdImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = replayMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            )
        );

        await sut.Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var stored = await verify
            .Set<FailToDeliver>()
            .OrderBy(record => record.SettlementDate)
            .ToListAsync();
        stored.Should().HaveCount(2);
        stored[0].SettlementDate.Should().Be(oldDate);
        stored[0].Quantity.Should().Be(777);
        stored[0].Price.Should().Be(180m);
        stored[1].SettlementDate.Should().Be(newDate);
        stored[1].Quantity.Should().Be(123);
        stored[1].Price.Should().Be(182m);
    }

    [Fact]
    public async Task Import_IdentityPreparationFails_StillImportsNewRecords()
    {
        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1).AddDays(-1);
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var csv =
            "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE\n"
            + $"{settlementDate:yyyyMMdd}|037833100|AAPL|12345|APPLE INC|187.50\n";
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream>(BuildFtdZipStream(csv)));
        var scopeFactory = CreateScopeFactory(Substitute.For<IBus>(), failingScope: 4);
        var errorReporter = new ErrorReporter(
            scopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var sut = new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            errorReporter,
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = settlementDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            )
        );

        await sut.Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var stored = await verify.Set<FailToDeliver>().SingleAsync();
        stored.CommonStockId.Should().Be(stock.Id);
        stored.SettlementDate.Should().Be(settlementDate);
        stored.Quantity.Should().Be(12345);
        var report = await verify.Set<Error>().SingleAsync();
        report.Source.Should().Be(ErrorSource.FtdScraper);
        report.Context.Should().Be("FtdImport.SeedCusips");
        report.Message.Should().Contain("Simulated identity-query failure");
    }

    private static Stream BuildFtdZipStream(string csvBody)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("cnsfails.txt");
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(csvBody);
            stream.Write(bytes, 0, bytes.Length);
        }
        buffer.Position = 0;
        return buffer;
    }
}

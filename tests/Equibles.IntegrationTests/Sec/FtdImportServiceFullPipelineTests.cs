using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
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
    private readonly List<EquiblesDbContext> _contexts = [];

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

    private EquiblesDbContext FreshContext()
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
    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesDbContext)).Returns(ctx);
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(
                            new CommonStockRepository(ctx),
                            Substitute.For<IPublishEndpoint>()
                        )
                    );
                sp.GetService(typeof(FailToDeliverRepository))
                    .Returns(new FailToDeliverRepository(ctx));
                sp.GetService(typeof(TickerMapService)).Returns(new TickerMapService(scopeFactory));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
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

using Equibles.Cboe.Data.Models;
using Equibles.Cboe.HostedService.Services;
using Equibles.Cboe.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Cboe;

/// <summary>
/// Cftc and Holdings each have a <c>*ImportServiceFullPipelineTests</c> against the shared
/// ParadeDB fixture; <see cref="CboeImportService"/> is the only remaining HostedService
/// importer without one. The DB-touching phases — <c>GetLatestDate</c> on both
/// <see cref="CboePutCallRatioRepository"/> and <see cref="CboeVixDailyRepository"/>,
/// the "filter records newer than stored" branch, and the batched <c>FlushPutCallBatch</c>
/// / <c>FlushVixBatch</c> saves through per-scope <see cref="EquiblesFinancialDbContext"/> instances
/// — are not reachable from the sibling <c>Equibles.UnitTests.Cboe.CboeImportServiceTests</c>
/// file. A regression in any of them would silently drop CBOE data on every worker tick.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CboeImportServiceFullPipelineTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public CboeImportServiceFullPipelineTests(ParadeDbFixture fixture)
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
    /// the same ParadeDB instance — mirroring production DI's scoped-DbContext lifetime
    /// so the importer's two repositories don't fight over the same change-tracker.
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
                sp.GetService(typeof(CboePutCallRatioRepository))
                    .Returns(new CboePutCallRatioRepository(ctx));
                sp.GetService(typeof(CboeVixDailyRepository))
                    .Returns(new CboeVixDailyRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    [Fact]
    public async Task Import_DownloadsAndPersistsBothPutCallRatiosAndVixHistoryToRealPostgres()
    {
        var client = Substitute.For<ICboeClient>();
        // Equity carries the put/call assertion; other csv types return empty so the
        // loop over the type-mapping exercises both the "no data" early-exit and the
        // happy persistence path without bloating the test.
        client
            .DownloadPutCallRatios(CboePutCallCsvType.Equity)
            .Returns([
                new CboePutCallRecord
                {
                    Date = new DateOnly(2026, 4, 1),
                    CallVolume = 1_200_000,
                    PutVolume = 800_000,
                    TotalVolume = 2_000_000,
                    PutCallRatio = 0.67m,
                },
                new CboePutCallRecord
                {
                    Date = new DateOnly(2026, 4, 2),
                    CallVolume = 1_500_000,
                    PutVolume = 1_100_000,
                    TotalVolume = 2_600_000,
                    PutCallRatio = 0.73m,
                },
            ]);
        client.DownloadPutCallRatios(CboePutCallCsvType.Total).Returns([]);
        client.DownloadPutCallRatios(CboePutCallCsvType.Index).Returns([]);
        client.DownloadPutCallRatios(CboePutCallCsvType.Vix).Returns([]);
        client.DownloadPutCallRatios(CboePutCallCsvType.Etp).Returns([]);
        client
            .DownloadVixHistory()
            .Returns([
                new CboeVixRecord
                {
                    Date = new DateOnly(2026, 4, 1),
                    Open = 14.20m,
                    High = 15.30m,
                    Low = 13.80m,
                    Close = 14.95m,
                },
            ]);

        var sut = new CboeImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            // ErrorReporter is a real class but its Report() only runs on the catch
            // path; happy-path Import never invokes it, so a substituted scope factory
            // is enough here.
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();

        var persistedEquity = await verify
            .Set<CboePutCallRatio>()
            .Where(r => r.RatioType == CboePutCallRatioType.Equity)
            .OrderBy(r => r.Date)
            .ToListAsync();
        persistedEquity
            .Should()
            .HaveCount(2, "both Equity records should round-trip through FlushPutCallBatch");
        persistedEquity[0].Date.Should().Be(new DateOnly(2026, 4, 1));
        persistedEquity[0].PutCallRatio.Should().Be(0.67m);
        persistedEquity[1].PutCallRatio.Should().Be(0.73m);

        // Types with empty downloads must NOT produce rows — the early-exit branch is
        // what stops the importer from inserting phantom defaults on every tick.
        var otherTypeCount = await verify
            .Set<CboePutCallRatio>()
            .Where(r => r.RatioType != CboePutCallRatioType.Equity)
            .CountAsync();
        otherTypeCount.Should().Be(0);

        var persistedVix = await verify.Set<CboeVixDaily>().ToListAsync();
        persistedVix.Should().ContainSingle();
        persistedVix[0].Close.Should().Be(14.95m);
    }

    // ── Idempotency + per-source resilience (zero-hit branches) ─────────

    private static ICboeClient NoPutCallClient()
    {
        var client = Substitute.For<ICboeClient>();
        foreach (var t in Enum.GetValues<CboePutCallCsvType>())
            client.DownloadPutCallRatios(t).Returns([]);
        return client;
    }

    [Fact]
    public async Task Import_VixHistoryAlreadyUpToDate_InsertsNothing()
    {
        using (var seed = FreshContext())
        {
            seed.Set<CboeVixDaily>()
                .Add(
                    new CboeVixDaily
                    {
                        Date = new DateOnly(2026, 4, 10),
                        Open = 14m,
                        High = 15m,
                        Low = 13m,
                        Close = 14.5m,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var client = NoPutCallClient();
        client
            .DownloadVixHistory()
            .Returns([
                new CboeVixRecord
                {
                    Date = new DateOnly(2026, 4, 1), // older than stored → filtered out
                    Open = 1m,
                    High = 1m,
                    Low = 1m,
                    Close = 1m,
                },
            ]);

        var sut = new CboeImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();
        var rows = await verify.Set<CboeVixDaily>().ToListAsync();
        rows.Should().ContainSingle("the only row is the pre-seeded one — nothing new inserted");
        rows[0].Close.Should().Be(14.5m);
    }

    [Fact]
    public async Task Import_VixDownloadThrowsHttp_SwallowsAndPersistsNothing()
    {
        var client = NoPutCallClient();
        client
            .DownloadVixHistory()
            .Returns<Task<List<CboeVixRecord>>>(_ => throw new HttpRequestException("CBOE 503"));

        var sut = new CboeImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();
        (await verify.Set<CboeVixDaily>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_VixDownloadThrowsGeneric_ReportsToErrorReporter()
    {
        var client = NoPutCallClient();
        client
            .DownloadVixHistory()
            .Returns<Task<List<CboeVixRecord>>>(_ => throw new InvalidOperationException("boom"));

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new CboeImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            errorReporter
        );

        await sut.Import(CancellationToken.None);

        await errorReporter
            .Received()
            .Report(
                ErrorSource.CboeScraper,
                "CboeImport.ImportVixHistory",
                Arg.Any<string>(),
                Arg.Any<string>()
            );
    }

    [Fact]
    public async Task Import_PutCallRatioAlreadyUpToDate_InsertsNothingForThatType()
    {
        using (var seed = FreshContext())
        {
            seed.Set<CboePutCallRatio>()
                .Add(
                    new CboePutCallRatio
                    {
                        RatioType = CboePutCallRatioType.Equity,
                        Date = new DateOnly(2026, 4, 10),
                        CallVolume = 1,
                        PutVolume = 1,
                        TotalVolume = 2,
                        PutCallRatio = 1m,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var client = NoPutCallClient();
        client
            .DownloadPutCallRatios(CboePutCallCsvType.Equity)
            .Returns([
                new CboePutCallRecord
                {
                    Date = new DateOnly(2026, 4, 1), // older than stored → filtered out
                    CallVolume = 5,
                    PutVolume = 5,
                    TotalVolume = 10,
                    PutCallRatio = 1m,
                },
            ]);
        client.DownloadVixHistory().Returns([]);

        var sut = new CboeImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();
        var rows = await verify
            .Set<CboePutCallRatio>()
            .Where(r => r.RatioType == CboePutCallRatioType.Equity)
            .ToListAsync();
        rows.Should().ContainSingle("the stored Equity row is current — no newer record inserted");
    }
}

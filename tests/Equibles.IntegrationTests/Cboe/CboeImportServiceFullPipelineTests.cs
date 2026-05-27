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
/// The DB-touching phases — per-type <c>GetLatestDate</c> probes, the
/// "filter records newer than stored" branch, and the batched persistence
/// through per-scope <see cref="EquiblesFinancialDbContext"/> instances —
/// are not reachable from sibling unit tests. A regression in any of them
/// would silently drop CBOE data on every worker tick.
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

    private static CboeImportService CreateSut(
        IServiceScopeFactory scopeFactory,
        ICboeClient client,
        DateOnly today,
        ErrorReporter errorReporter = null
    ) =>
        new(
            scopeFactory,
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            errorReporter
                ?? new ErrorReporter(
                    Substitute.For<IServiceScopeFactory>(),
                    Substitute.For<ILogger<ErrorReporter>>()
                ),
            () => today
        );

    private static ICboeClient EmptyClient()
    {
        var client = Substitute.For<ICboeClient>();
        client
            .DownloadDailyPutCallRatios(Arg.Any<DateOnly>())
            .Returns(new Dictionary<CboePutCallProductType, CboePutCallRecord>());
        client.DownloadVixHistory().Returns(new List<CboeVixRecord>());
        return client;
    }

    private static CboePutCallRatio Seed(CboePutCallRatioType type, DateOnly date) =>
        new()
        {
            RatioType = type,
            Date = date,
            CallVolume = 1,
            PutVolume = 1,
            TotalVolume = 2,
            PutCallRatio = 1m,
        };

    private async Task SeedAllTypesAt(DateOnly date)
    {
        using var seed = FreshContext();
        foreach (var t in Enum.GetValues<CboePutCallRatioType>())
            seed.Set<CboePutCallRatio>().Add(Seed(t, date));
        await seed.SaveChangesAsync();
    }

    [Fact]
    public async Task Import_DownloadsAndPersistsBothPutCallRatiosAndVixHistoryToRealPostgres()
    {
        // A single-day catch-up from a seeded baseline. Verifies the full
        // pipeline: per-type GetLatestDate → daily-page fetch → per-product
        // filter → batched persistence — all touching real ParadeDB.
        await SeedAllTypesAt(new DateOnly(2026, 3, 31));

        var today = new DateOnly(2026, 4, 1); // Wednesday
        var client = Substitute.For<ICboeClient>();
        client
            .DownloadDailyPutCallRatios(today)
            .Returns(
                new Dictionary<CboePutCallProductType, CboePutCallRecord>
                {
                    [CboePutCallProductType.Equity] = new()
                    {
                        Date = today,
                        CallVolume = 1_200_000,
                        PutVolume = 800_000,
                        TotalVolume = 2_000_000,
                        PutCallRatio = 0.67m,
                    },
                    [CboePutCallProductType.Total] = new()
                    {
                        Date = today,
                        CallVolume = 3_000_000,
                        PutVolume = 2_500_000,
                        TotalVolume = 5_500_000,
                        PutCallRatio = 0.83m,
                    },
                }
            );
        client
            .DownloadVixHistory()
            .Returns([
                new CboeVixRecord
                {
                    Date = today,
                    Open = 14.20m,
                    High = 15.30m,
                    Low = 13.80m,
                    Close = 14.95m,
                },
            ]);

        var sut = CreateSut(CreateScopeFactory(), client, today);

        await sut.Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();

        var equityRows = await verify
            .Set<CboePutCallRatio>()
            .Where(r => r.RatioType == CboePutCallRatioType.Equity)
            .OrderBy(r => r.Date)
            .ToListAsync();
        equityRows
            .Should()
            .HaveCount(2, "the seeded row and the one new daily-page row both persist");
        equityRows[1].Date.Should().Be(today);
        equityRows[1].PutCallRatio.Should().Be(0.67m);

        var totalRows = await verify
            .Set<CboePutCallRatio>()
            .Where(r => r.RatioType == CboePutCallRatioType.Total)
            .OrderBy(r => r.Date)
            .ToListAsync();
        totalRows.Should().HaveCount(2);
        totalRows[1].PutCallRatio.Should().Be(0.83m);

        // Types absent from the day's response must not gain phantom rows.
        var indexCount = await verify
            .Set<CboePutCallRatio>()
            .Where(r => r.RatioType == CboePutCallRatioType.Index)
            .CountAsync();
        indexCount.Should().Be(1, "only the seeded row remains for Index");

        var persistedVix = await verify.Set<CboeVixDaily>().ToListAsync();
        persistedVix.Should().ContainSingle();
        persistedVix[0].Close.Should().Be(14.95m);
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

        var client = EmptyClient();
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

        var sut = CreateSut(CreateScopeFactory(), client, new DateOnly(2026, 4, 10));

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();
        var rows = await verify.Set<CboeVixDaily>().ToListAsync();
        rows.Should().ContainSingle("the only row is the pre-seeded one — nothing new inserted");
        rows[0].Close.Should().Be(14.5m);
    }

    [Fact]
    public async Task Import_VixDownloadThrowsHttp_SwallowsAndPersistsNothing()
    {
        var client = EmptyClient();
        client
            .DownloadVixHistory()
            .Returns<Task<List<CboeVixRecord>>>(_ => throw new HttpRequestException("CBOE 503"));

        var sut = CreateSut(CreateScopeFactory(), client, new DateOnly(2026, 4, 10));

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();
        (await verify.Set<CboeVixDaily>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_VixDownloadThrowsGeneric_ReportsToErrorReporter()
    {
        var client = EmptyClient();
        client
            .DownloadVixHistory()
            .Returns<Task<List<CboeVixRecord>>>(_ => throw new InvalidOperationException("boom"));

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = CreateSut(CreateScopeFactory(), client, new DateOnly(2026, 4, 10), errorReporter);

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
    public async Task Import_PutCallRatioAlreadyUpToDate_DoesNotCallCboeClient()
    {
        // Seed every type at today's date — earliestKnown equals today, so
        // start > today and the date loop never iterates.
        var today = new DateOnly(2026, 4, 10);
        await SeedAllTypesAt(today);

        var client = EmptyClient();

        var sut = CreateSut(CreateScopeFactory(), client, today);

        await sut.Import(CancellationToken.None);

        await client.DidNotReceive().DownloadDailyPutCallRatios(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task Import_SkipsWeekendsLocally()
    {
        // Local weekend skip avoids burning the rate-limit budget on a feed
        // CBOE doesn't publish Sat/Sun. With seed = Thu 2026-04-09 and
        // today = Mon 2026-04-13, the loop covers Fri-Mon but only Fri/Mon
        // hit the CDN.
        await SeedAllTypesAt(new DateOnly(2026, 4, 9));

        var client = EmptyClient();
        var sut = CreateSut(CreateScopeFactory(), client, new DateOnly(2026, 4, 13));

        await sut.Import(CancellationToken.None);

        await client.Received().DownloadDailyPutCallRatios(new DateOnly(2026, 4, 10));
        await client.Received().DownloadDailyPutCallRatios(new DateOnly(2026, 4, 13));
        await client.DidNotReceive().DownloadDailyPutCallRatios(new DateOnly(2026, 4, 11));
        await client.DidNotReceive().DownloadDailyPutCallRatios(new DateOnly(2026, 4, 12));
    }

    [Fact]
    public async Task Import_PutCallDownloadThrowsHttp_LogsAndContinuesToNextDay()
    {
        // A single-day download blip must not abort the catch-up window.
        // The next day's data is still worth fetching, and the error must
        // NOT escalate to ErrorReporter (HttpRequestException is the
        // transient-blip signal).
        var today = new DateOnly(2026, 4, 13); // Monday
        await SeedAllTypesAt(new DateOnly(2026, 4, 9));

        var failingDate = new DateOnly(2026, 4, 10);
        var client = Substitute.For<ICboeClient>();
        client
            .DownloadDailyPutCallRatios(failingDate)
            .Returns<Task<Dictionary<CboePutCallProductType, CboePutCallRecord>>>(_ =>
                throw new HttpRequestException("CBOE 503")
            );
        client
            .DownloadDailyPutCallRatios(today)
            .Returns(
                new Dictionary<CboePutCallProductType, CboePutCallRecord>
                {
                    [CboePutCallProductType.Total] = new()
                    {
                        Date = today,
                        CallVolume = 1,
                        PutVolume = 1,
                        TotalVolume = 2,
                        PutCallRatio = 1.5m,
                    },
                }
            );
        client.DownloadVixHistory().Returns(new List<CboeVixRecord>());

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = CreateSut(CreateScopeFactory(), client, today, errorReporter);

        await sut.Import(CancellationToken.None);

        using var verify = FreshContext();
        var totalRows = await verify
            .Set<CboePutCallRatio>()
            .Where(r => r.RatioType == CboePutCallRatioType.Total)
            .OrderBy(r => r.Date)
            .ToListAsync();
        totalRows.Should().HaveCount(2, "the failing Friday is skipped but Monday still persists");
        totalRows[1].Date.Should().Be(today);

        await errorReporter
            .DidNotReceive()
            .Report(
                ErrorSource.CboeScraper,
                "CboeImport.ImportPutCallRatio",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
    }

    [Fact]
    public async Task Import_PutCallDownloadThrowsGeneric_ReportsAndAbortsCycle()
    {
        // A non-HTTP exception signals the page shape changed or a parser
        // bug — continuing the loop would burn the rate-limit budget on
        // identical failures. The contract: report once, then bail.
        var today = new DateOnly(2026, 4, 14); // Tuesday
        await SeedAllTypesAt(new DateOnly(2026, 4, 9));

        var client = Substitute.For<ICboeClient>();
        client
            .DownloadDailyPutCallRatios(Arg.Any<DateOnly>())
            .Returns<Task<Dictionary<CboePutCallProductType, CboePutCallRecord>>>(_ =>
                throw new InvalidOperationException("page shape changed")
            );
        client.DownloadVixHistory().Returns(new List<CboeVixRecord>());

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = CreateSut(CreateScopeFactory(), client, today, errorReporter);

        await sut.Import(CancellationToken.None);

        await client.Received(1).DownloadDailyPutCallRatios(Arg.Any<DateOnly>());
        await errorReporter
            .Received()
            .Report(
                ErrorSource.CboeScraper,
                "CboeImport.ImportPutCallRatio",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
    }
}

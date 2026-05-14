using Equibles.Cboe.Data.Models;
using Equibles.Cboe.HostedService.Services;
using Equibles.Cboe.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
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
/// / <c>FlushVixBatch</c> saves through per-scope <see cref="EquiblesDbContext"/> instances
/// — are not reachable from the sibling <c>Equibles.UnitTests.Cboe.CboeImportServiceTests</c>
/// file. A regression in any of them would silently drop CBOE data on every worker tick.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CboeImportServiceFullPipelineTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

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

    private EquiblesDbContext FreshContext()
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
}

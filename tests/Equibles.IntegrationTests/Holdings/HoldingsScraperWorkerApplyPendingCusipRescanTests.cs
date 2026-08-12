using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for the deferred CUSIP-identity rescan (EquiblesCommercial#7163):
/// the worker — not the consumer — applies a queued rescan at cycle start, so
/// an in-flight multi-hour walk is never restarted by a mid-walk discovery.
/// Applying clears the quarterly ledger (keeping exactly one backfill guard)
/// and the OPEN SEASON's realtime per-accession rows, because the bulk walk
/// can only restate filings a published quarterly data set covers — the open
/// season's submissions exist only behind the realtime ledger and would
/// otherwise keep pre-discovery holes until that data set publishes.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerApplyPendingCusipRescanTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsScraperWorkerApplyPendingCusipRescanTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private Equibles.Data.EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private HoldingsScraperWorker BuildWorker()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            new ConfigurationBuilder().Build(),
            new HoldingsRescanSignal()
        );
    }

    private IServiceScope CreateScopeFromFixture()
    {
        var ctx = FreshContext();
        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider
            .GetService(typeof(ProcessedDataSetRepository))
            .Returns(new ProcessedDataSetRepository(ctx));
        provider
            .GetService(typeof(ProcessedFilingRepository))
            .Returns(new ProcessedFilingRepository(ctx));
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    private static DateTime InSeason() => DateTime.UtcNow;

    private static DateTime BeforeSeason() =>
        DateTime.SpecifyKind(new DateTime(2020, 1, 15), DateTimeKind.Utc);

    [Fact]
    public async Task Apply_SentinelQueued_ClearsLedgerKeepsGuardAndClearsOpenSeasonFilings()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .AddRange(
                    new ProcessedDataSet { FileName = ProcessedDataSet.RescanPendingFileName },
                    new ProcessedDataSet { FileName = ProcessedDataSet.BackfillGuardFileName },
                    new ProcessedDataSet
                    {
                        FileName = "01mar2025-31may2025_form13f.zip",
                        SubmissionCount = 7987,
                    },
                    new ProcessedDataSet
                    {
                        FileName = "01dec2025-28feb2026_form13f.zip",
                        SubmissionCount = 8943,
                    }
                );
            seed.Set<ProcessedFiling>()
                .AddRange(
                    new ProcessedFiling
                    {
                        AccessionNumber = "0000000000-26-000001",
                        CreationTime = InSeason(),
                    },
                    new ProcessedFiling
                    {
                        AccessionNumber = "0000000000-20-000001",
                        CreationTime = BeforeSeason(),
                    }
                );
            await seed.SaveChangesAsync();
        }

        await BuildWorker().ApplyPendingCusipRescan(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var dataSets = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        dataSets
            .Should()
            .ContainSingle("the rescan clears every real row and the sentinel itself")
            .Which.Should()
            .Be(ProcessedDataSet.BackfillGuardFileName);

        // The open-season realtime accession re-imports; older accessions are
        // covered by the bulk data sets the walk re-processes anyway.
        var filings = await verify
            .Set<ProcessedFiling>()
            .Select(f => f.AccessionNumber)
            .ToListAsync();
        filings.Should().ContainSingle().Which.Should().Be("0000000000-20-000001");
    }

    [Fact]
    public async Task Apply_NoSentinel_IsNoOp()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .AddRange(
                    new ProcessedDataSet { FileName = ProcessedDataSet.BackfillGuardFileName },
                    new ProcessedDataSet
                    {
                        FileName = "01mar2025-31may2025_form13f.zip",
                        SubmissionCount = 7987,
                    }
                );
            seed.Set<ProcessedFiling>()
                .Add(
                    new ProcessedFiling
                    {
                        AccessionNumber = "0000000000-26-000001",
                        CreationTime = InSeason(),
                    }
                );
            await seed.SaveChangesAsync();
        }

        await BuildWorker().ApplyPendingCusipRescan(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<ProcessedDataSet>().CountAsync()).Should().Be(2);
        (await verify.Set<ProcessedFiling>().CountAsync()).Should().Be(1);
    }

    // A sentinel can be queued before the guard ever existed (fresh install
    // whose first identity discovery precedes the first walk). Applying must
    // still leave exactly one guard so BackfillProcessedDataSets does not
    // re-seed history as processed.
    [Fact]
    public async Task Apply_SentinelWithoutGuard_AddsTheGuard()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<ProcessedDataSet>()
                .AddRange(
                    new ProcessedDataSet { FileName = ProcessedDataSet.RescanPendingFileName },
                    new ProcessedDataSet
                    {
                        FileName = "01mar2025-31may2025_form13f.zip",
                        SubmissionCount = 7987,
                    }
                );
            await seed.SaveChangesAsync();
        }

        await BuildWorker().ApplyPendingCusipRescan(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var dataSets = await verify.Set<ProcessedDataSet>().Select(r => r.FileName).ToListAsync();
        dataSets.Should().ContainSingle().Which.Should().Be(ProcessedDataSet.BackfillGuardFileName);
    }
}

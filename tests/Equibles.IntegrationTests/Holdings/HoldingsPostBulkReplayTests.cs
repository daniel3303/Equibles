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

// Durable post-bulk replay must survive restarts and commit atomically with the epoch reset.
[Collection(ParadeDbCollection.Name)]
public class HoldingsPostBulkReplayTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsPostBulkReplayTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private readonly HoldingsRealtimeReplaySignal _signal = new();
    private bool _omitFilings;

    private HoldingsScraperWorker BuildWorker(DateTime? historyStart = null)
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
            Options.Create(new WorkerOptions { MinSyncDate = historyStart }),
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
        if (!_omitFilings)
            provider
                .GetService(typeof(ProcessedFilingRepository))
                .Returns(new ProcessedFilingRepository(ctx));
        provider.GetService(typeof(HoldingsRealtimeReplaySignal)).Returns(_signal);
        provider
            .GetService(typeof(RealtimeSweepStateRepository))
            .Returns(new RealtimeSweepStateRepository(ctx));
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    private async Task Seed(bool pending = true, DateOnly? watermark = null)
    {
        await using var db = _fixture.CreateDbContext();
        db.Add(
            new ProcessedDataSet
            {
                FileName = "01jun2026-31aug2026_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
                SubmissionCount = 9000,
            }
        );
        db.Add(
            new RealtimeSweepState
            {
                WorkerName = "Holdings13FRealtime",
                SweptThrough = watermark ?? new DateOnly(2026, 9, 24),
                UpdatedAt = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        db.Add(
            new ProcessedFiling
            {
                AccessionNumber = "older",
                CreationTime = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        db.Add(
            new ProcessedFiling
            {
                AccessionNumber = "newer",
                CreationTime = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();
        if (pending)
        {
            var repository = new ProcessedDataSetRepository(db);
            await repository.QueueRealtimeReplay(CancellationToken.None);
            await repository.QueueRealtimeReplay(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RestartAfterBulk_ReopensLaterFilingsAndWakesRealtime()
    {
        await Seed();
        // A new worker/scope recovers durable intent from the preceding process.
        await BuildWorker().ApplyPendingRealtimeReplay(CancellationToken.None);
        await using var db = _fixture.CreateDbContext();
        var state = await db.Set<RealtimeSweepState>().SingleAsync();
        state.SweptThrough.Should().Be(new DateOnly(2026, 9, 1));
        state.UpdatedAt.Should().NotBe(new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc));
        (await db.Set<ProcessedFiling>().Select(f => f.AccessionNumber).ToListAsync())
            .Should()
            .Equal("older");
        (await db.Set<ProcessedDataSet>().Select(f => f.FileName).ToListAsync())
            .Should()
            .Equal("01jun2026-31aug2026_form13f.zip");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _signal.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task BulkReplay_DoesNotSkipAnEarlierPendingRealtimeGap()
    {
        await Seed(watermark: new DateOnly(2026, 6, 1));
        await BuildWorker().ApplyPendingRealtimeReplay(CancellationToken.None);
        await using var db = _fixture.CreateDbContext();
        (await db.Set<RealtimeSweepState>().SingleAsync())
            .SweptThrough.Should()
            .Be(new DateOnly(2026, 6, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2024)]
    public async Task NoCompletedArchive_ReplaysTheConfiguredHistoryAfterPartialWrites(
        int configuredYear
    )
    {
        await Seed();
        await using (var seed = _fixture.CreateDbContext())
            await seed.Set<ProcessedDataSet>()
                .Where(p => p.FileName != ProcessedDataSet.RealtimeReplayPendingFileName)
                .ExecuteDeleteAsync();
        await BuildWorker(configuredYear == 0 ? null : new DateTime(configuredYear, 1, 1))
            .ApplyPendingRealtimeReplay(CancellationToken.None);
        await using var db = _fixture.CreateDbContext();
        (await db.Set<RealtimeSweepState>().SingleAsync())
            .SweptThrough.Should()
            .Be(new DateOnly(configuredYear == 0 ? 2020 : configuredYear, 1, 1));
        (await db.Set<ProcessedFiling>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task IdentityRescan_PreservesAnUnfinishedPostBulkReplay()
    {
        await Seed();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(new ProcessedDataSet { FileName = ProcessedDataSet.RescanPendingFileName });
            await seed.SaveChangesAsync();
        }
        await BuildWorker().ApplyPendingCusipRescan(CancellationToken.None);
        await using var db = _fixture.CreateDbContext();
        (
            await db.Set<ProcessedDataSet>()
                .AnyAsync(p => p.FileName == ProcessedDataSet.RealtimeReplayPendingFileName)
        )
            .Should()
            .BeTrue();
        (
            await db.Set<ProcessedDataSet>()
                .AnyAsync(p => p.FileName == "01jun2026-31aug2026_form13f.zip")
        )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task NoBulkReplay_DoesNotClearRealtimeProgress()
    {
        await Seed(pending: false);
        await BuildWorker().ApplyPendingRealtimeReplay(CancellationToken.None);
        await using var db = _fixture.CreateDbContext();
        (await db.Set<RealtimeSweepState>().SingleAsync())
            .SweptThrough.Should()
            .Be(new DateOnly(2026, 9, 24));
        (await db.Set<ProcessedFiling>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ReplayFailure_RollsBackWatermarkAndRetainsDurableIntent()
    {
        await Seed();
        _omitFilings = true;
        var apply = () => BuildWorker().ApplyPendingRealtimeReplay(CancellationToken.None);
        await apply.Should().ThrowAsync<InvalidOperationException>();
        await using var db = _fixture.CreateDbContext();
        (await db.Set<RealtimeSweepState>().SingleAsync())
            .SweptThrough.Should()
            .Be(new DateOnly(2026, 9, 24));
        (await db.Set<ProcessedFiling>().CountAsync()).Should().Be(2);
        (
            await db.Set<ProcessedDataSet>()
                .CountAsync(p => p.FileName == ProcessedDataSet.RealtimeReplayPendingFileName)
        )
            .Should()
            .Be(1);
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: <see cref="AumSnapshotRebuildWorker"/> backfills every quarter
/// on first boot when the snapshot tables are empty, and on subsequent ticks
/// re-runs the rebuild as a safety net.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class AumSnapshotRebuildWorkerTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public AumSnapshotRebuildWorkerTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private static readonly DateOnly Q3 = new(2024, 9, 30);
    private static readonly DateOnly Q4 = new(2024, 12, 31);

    private IServiceScopeFactory ScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var scope = Substitute.For<IServiceScope>();
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(Equibles.Data.EquiblesFinancialDbContext)).Returns(ctx);
                provider
                    .GetService(typeof(AumQuarterlySnapshotRepository))
                    .Returns(_ => new AumQuarterlySnapshotRepository(ctx));
                provider
                    .GetService(typeof(SectorQuarterlySnapshotRepository))
                    .Returns(_ => new SectorQuarterlySnapshotRepository(ctx));
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }

    // BackgroundService subclass that collapses both delays so the test can
    // poke ExecuteAsync without waiting 5 minutes for the startup gate and
    // 24 hours for the next cycle.
    private sealed class InstantTickWorker : AumSnapshotRebuildWorker
    {
        public InstantTickWorker(
            IServiceScopeFactory scopeFactory,
            HoldingsAggregateRefreshService refreshService
        )
            : base(scopeFactory, refreshService, NullLogger<AumSnapshotRebuildWorker>.Instance) { }

        protected override TimeSpan StartupDelay => TimeSpan.Zero;
        protected override TimeSpan SleepInterval => TimeSpan.FromMilliseconds(1);
    }

    [Fact]
    public async Task ExecuteAsync_EmptySnapshotsButHoldingsExist_BackfillsEveryQuarter()
    {
        await SeedTwoQuarters();

        var scopeFactory = ScopeFactory();
        var refreshService = new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
        var worker = new InstantTickWorker(scopeFactory, refreshService);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Run one cycle: the backfill, then one safety-net pass. Cancel after
        // a brief delay so the loop exits.
        var run = worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            // Expected — the loop exited via the stoppingToken.
        }

        await using var read = FreshContext();
        var snapshots = await read.Set<AumQuarterlySnapshot>()
            .OrderBy(s => s.ReportDate)
            .ToListAsync();

        snapshots.Should().HaveCount(2);
        snapshots[0].ReportDate.Should().Be(Q3);
        snapshots[0].TotalValue.Should().Be(100_000);
        snapshots[1].ReportDate.Should().Be(Q4);
        snapshots[1].TotalValue.Should().Be(200_000);
    }

    [Fact]
    public async Task ExecuteAsync_NoHoldings_DoesNotCreatePhantomSnapshotRows()
    {
        // No holdings seeded — the worker should not invent rows out of nothing.
        var scopeFactory = ScopeFactory();
        var refreshService = new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
        var worker = new InstantTickWorker(scopeFactory, refreshService);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), cts.Token);
        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException) { }

        await using var read = FreshContext();
        (await read.Set<AumQuarterlySnapshot>().AnyAsync()).Should().BeFalse();
    }

    private async Task SeedTwoQuarters()
    {
        await using var seed = FreshContext();
        var tech = new Sector { Name = "Technology" };
        seed.Add(tech);
        await seed.SaveChangesAsync();
        var industry = new Industry { Name = "Software", SectorId = tech.Id };
        seed.Add(industry);
        await seed.SaveChangesAsync();
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "C0000320193",
            IndustryId = industry.Id,
        };
        seed.Add(aapl);
        var holder = new InstitutionalHolder { Cik = "H001", Name = "Holder H001" };
        seed.Add(holder);
        await seed.SaveChangesAsync();
        seed.AddRange(
            MakeHolding(aapl, holder, Q3, 100_000, "acc-q3"),
            MakeHolding(aapl, holder, Q4, 200_000, "acc-q4")
        );
        await seed.SaveChangesAsync();
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long value,
        string accession
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
            Cusip =
                $"{stock.Ticker[..Math.Min(4, stock.Ticker.Length)]}{stock.Id.GetHashCode():X8}"[
                    ..9
                ],
        };
}

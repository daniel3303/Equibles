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

    // The worker backfills on a 1ms loop, so snapshot rows land asynchronously
    // after StartAsync. Poll a fresh context until the expected state appears
    // rather than sleeping a fixed interval — the old 500ms wait flaked when the
    // backfill outlasted it on a slow host or a cold test container (#3780).
    private async Task WaitForSnapshots(
        Func<Equibles.Data.EquiblesFinancialDbContext, Task<bool>> ready
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using (var ctx = _fixture.CreateDbContext())
            {
                if (await ready(ctx))
                    return;
            }

            if (DateTime.UtcNow > deadline)
                return; // let the assertion report the shortfall with detail

            await Task.Delay(25);
        }
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
        // Run one cycle: the backfill, then one safety-net pass.
        // StopAsync signals the worker's internal stopping token (cancelling
        // cts here doesn't reach it — it only governs the startup phase) and
        // awaits ExecuteAsync, so contexts created by the loop are guaranteed
        // to be idle by the time DisposeAsync disposes them.
        await worker.StartAsync(cts.Token);
        await WaitForSnapshots(async ctx =>
            await ctx.Set<AumQuarterlySnapshot>().CountAsync() >= 2
        );
        await worker.StopAsync(CancellationToken.None);

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
    public async Task ExecuteAsync_PartialSnapshotCoverage_BackfillsMissingQuarters()
    {
        // The realtime consumer can beat the worker to inserting a snapshot
        // row for the current quarter. The naive "snapshot table empty" gate
        // would skip the historical backfill in that case. With the coverage
        // check, the worker sees one snapshot covering two holding quarters
        // and runs the backfill anyway, picking up the missing quarter.
        await SeedTwoQuarters();
        await using (var seed = FreshContext())
        {
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 999_999_999, // sentinel — must get overwritten
                    FilerCount = 99,
                    PositionCount = 99,
                    StockCount = 99,
                    FilingCount = 99,
                }
            );
            await seed.SaveChangesAsync();
        }

        var scopeFactory = ScopeFactory();
        var refreshService = new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
        var worker = new InstantTickWorker(scopeFactory, refreshService);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);
        // Wait for both quarters present AND the Q4 sentinel overwritten — the
        // upsert lands both in one backfill pass, so this is satisfied together.
        await WaitForSnapshots(async ctx =>
            await ctx.Set<AumQuarterlySnapshot>().CountAsync() >= 2
            && await ctx.Set<AumQuarterlySnapshot>()
                .AnyAsync(s => s.ReportDate == Q4 && s.TotalValue == 200_000)
        );
        await worker.StopAsync(CancellationToken.None);

        await using var read = FreshContext();
        var snapshots = await read.Set<AumQuarterlySnapshot>()
            .OrderBy(s => s.ReportDate)
            .ToListAsync();

        snapshots.Should().HaveCount(2);
        snapshots[0].ReportDate.Should().Be(Q3);
        snapshots[0].TotalValue.Should().Be(100_000);
        snapshots[1].ReportDate.Should().Be(Q4);
        snapshots[1]
            .TotalValue.Should()
            .Be(200_000, "the sentinel row must be overwritten by the rebuild");
    }

    [Fact]
    public async Task ExecuteAsync_HolderSnapshotsMissing_BackfillsDespiteFullAumCoverage()
    {
        // Deploy scenario for the holder snapshot table: AUM and activity
        // snapshots already cover every quarter, the newly-added holder table
        // is empty. The coverage gate must still trigger the full backfill —
        // the daily safety-net alone only reaches the 4 most recent quarters,
        // so without the gate the oldest quarter would never materialise.
        // Five quarters seeded so the oldest lies beyond the safety-net.
        var quarters = Enumerable
            .Range(0, 5)
            .Select(i => new DateOnly(2023, 12, 31).AddMonths(3 * i))
            .ToList();
        InstitutionalHolder holder;
        CommonStock aapl;
        await using (var seed = FreshContext())
        {
            var tech = new Sector { Name = "Technology" };
            seed.Add(tech);
            await seed.SaveChangesAsync();
            var industry = new Industry { Name = "Software", SectorId = tech.Id };
            seed.Add(industry);
            await seed.SaveChangesAsync();
            aapl = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple",
                Cik = "C0000320193",
                IndustryId = industry.Id,
            };
            seed.Add(aapl);
            holder = new InstitutionalHolder { Cik = "H001", Name = "Holder H001" };
            seed.Add(holder);
            await seed.SaveChangesAsync();
            foreach (var quarter in quarters)
            {
                seed.Add(MakeHolding(aapl, holder, quarter, 100_000, $"acc-{quarter}"));
                seed.Add(
                    new AumQuarterlySnapshot
                    {
                        ReportDate = quarter,
                        TotalValue = 100_000,
                        FilerCount = 1,
                        PositionCount = 1,
                        StockCount = 1,
                        FilingCount = 1,
                    }
                );
                seed.Add(
                    new StockQuarterlyActivity
                    {
                        CommonStockId = aapl.Id,
                        ReportDate = quarter,
                        CurrentShares = 1_000,
                        CurrentValue = 100_000,
                        CurrentFilerCount = 1,
                    }
                );
            }
            await seed.SaveChangesAsync();
        }

        var scopeFactory = ScopeFactory();
        var refreshService = new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
        var worker = new InstantTickWorker(scopeFactory, refreshService);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);
        await WaitForSnapshots(async ctx =>
            await ctx.Set<HolderQuarterlySnapshot>()
                .CountAsync(s => s.InstitutionalHolderId == holder.Id) >= quarters.Count
        );
        await worker.StopAsync(CancellationToken.None);

        await using var read = FreshContext();
        var holderQuarters = await read.Set<HolderQuarterlySnapshot>()
            .Where(s => s.InstitutionalHolderId == holder.Id)
            .Select(s => s.ReportDate)
            .OrderBy(d => d)
            .ToListAsync();
        holderQuarters
            .Should()
            .Equal(quarters, "the backfill covers every quarter, not just the safety-net window");
    }

    [Fact]
    public async Task ExecuteAsync_Schedule13DGEventDates_DoNotRetriggerFullBackfill()
    {
        // Schedule 13D/G rows carry per-day EVENT dates as ReportDate, so the
        // all-types distinct-quarter count always exceeds what the rebuild can
        // ever materialise (it enumerates 13F quarters only). The coverage
        // gate must ignore them — otherwise every boot re-runs the full
        // backfill forever. Five fully-covered 13F quarters are seeded so the
        // oldest lies beyond the 4-quarter daily safety-net: a sentinel there
        // survives iff the full backfill did NOT run.
        var quarters = Enumerable
            .Range(0, 5)
            .Select(i => new DateOnly(2023, 12, 31).AddMonths(3 * i))
            .ToList();
        var oldest = quarters[0];
        var recent = quarters.Skip(1).ToList();
        InstitutionalHolder holder;
        CommonStock aapl;
        await using (var seed = FreshContext())
        {
            var tech = new Sector { Name = "Technology" };
            seed.Add(tech);
            await seed.SaveChangesAsync();
            var industry = new Industry { Name = "Software", SectorId = tech.Id };
            seed.Add(industry);
            await seed.SaveChangesAsync();
            aapl = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple",
                Cik = "C0000320193",
                IndustryId = industry.Id,
            };
            seed.Add(aapl);
            holder = new InstitutionalHolder { Cik = "H001", Name = "Holder H001" };
            seed.Add(holder);
            await seed.SaveChangesAsync();
            foreach (var quarter in quarters)
            {
                seed.Add(MakeHolding(aapl, holder, quarter, 100_000, $"acc-{quarter}"));
                seed.Add(
                    new AumQuarterlySnapshot
                    {
                        ReportDate = quarter,
                        TotalValue = 999_999_999, // sentinel — only the safety-net window may overwrite it
                        FilerCount = 1,
                        PositionCount = 1,
                        StockCount = 1,
                        FilingCount = 1,
                    }
                );
                seed.Add(
                    new StockQuarterlyActivity
                    {
                        CommonStockId = aapl.Id,
                        ReportDate = quarter,
                        CurrentShares = 1_000,
                        CurrentValue = 100_000,
                        CurrentFilerCount = 1,
                    }
                );
                seed.Add(
                    new HolderQuarterlySnapshot
                    {
                        InstitutionalHolderId = holder.Id,
                        ReportDate = quarter,
                        FilingDate = quarter.AddDays(45),
                        Aum = 100_000,
                        PositionCount = 1,
                        StockCount = 1,
                    }
                );
            }
            // 13D/G event dates that are not 13F quarter ends — these must not
            // count towards the coverage the gate expects the rebuild to reach.
            seed.Add(
                MakeHolding(
                    aapl,
                    holder,
                    new DateOnly(2024, 2, 14),
                    50_000,
                    "acc-13d",
                    FilingType.Schedule13D
                )
            );
            seed.Add(
                MakeHolding(
                    aapl,
                    holder,
                    new DateOnly(2024, 11, 15),
                    60_000,
                    "acc-13g",
                    FilingType.Schedule13G
                )
            );
            await seed.SaveChangesAsync();
        }

        var scopeFactory = ScopeFactory();
        var refreshService = new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
        var worker = new InstantTickWorker(scopeFactory, refreshService);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);
        // Wait until the daily safety-net has rebuilt the 4 recent quarters
        // (their sentinels replaced by the true 100k total) so "the backfill
        // didn't run" is asserted after the worker has demonstrably cycled.
        await WaitForSnapshots(async ctx =>
            await ctx.Set<AumQuarterlySnapshot>()
                .CountAsync(s => recent.Contains(s.ReportDate) && s.TotalValue == 100_000)
            >= recent.Count
        );
        await worker.StopAsync(CancellationToken.None);

        await using var read = FreshContext();
        var oldestSnapshot = await read.Set<AumQuarterlySnapshot>()
            .SingleAsync(s => s.ReportDate == oldest);
        oldestSnapshot
            .TotalValue.Should()
            .Be(
                999_999_999,
                "the oldest quarter is outside the safety-net window, so only the full backfill could rewrite it — and full coverage of the 13F quarters means it must not have run"
            );
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
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), cts.Token);
        await worker.StopAsync(CancellationToken.None);

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
        string accession,
        FilingType filingType = FilingType.Form13F
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingType = filingType,
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

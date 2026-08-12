using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for <see cref="AumSnapshotDrainWorker"/>: each tick rebuilds the
/// snapshots whose <see cref="AumQuarterlySnapshot.DirtyAt"/> is older than
/// the cooldown and clears the flag. Events landing inside the cooldown
/// window coalesce into a single rebuild.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class AumSnapshotDrainWorkerTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public AumSnapshotDrainWorkerTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private static readonly DateOnly Q4 = new(2024, 12, 31);

    private IServiceScopeFactory BuildScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return scopeFactory;
    }

    private IServiceScope CreateScopeFromFixture()
    {
        var ctx = FreshContext();
        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(Equibles.Data.EquiblesFinancialDbContext)).Returns(ctx);
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    private HoldingsAggregateRefreshService BuildRefreshService() =>
        new(BuildScopeFactory(), NullLogger<HoldingsAggregateRefreshService>.Instance);

    private TestableDrainWorker BuildWorker(TimeSpan? cooldown = null) =>
        new(
            BuildScopeFactory(),
            BuildRefreshService(),
            NullLogger<AumSnapshotDrainWorker>.Instance,
            cooldown ?? TimeSpan.Zero
        );

    [Fact]
    public async Task DrainOnce_NoDirtyRows_DoesNothing()
    {
        await SeedHoldings();
        await using (var seed = FreshContext())
        {
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 999L,
                    DirtyAt = null,
                }
            );
            await seed.SaveChangesAsync();
        }

        await BuildWorker().DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.TotalValue.Should().Be(999L, "no rebuild ran — stale stub value is intact");
    }

    [Fact]
    public async Task DrainOnce_DirtyAndPastCooldown_RebuildsAndClearsDirtyAt()
    {
        await SeedHoldings();
        await using (var seed = FreshContext())
        {
            // Stale stub TotalValue forces the assertion to verify a real rebuild ran.
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 999L,
                    FilerCount = 0,
                    PositionCount = 0,
                    StockCount = 0,
                    FilingCount = 0,
                    DirtyAt = DateTime.UtcNow.AddHours(-2),
                }
            );
            await seed.SaveChangesAsync();
        }

        await BuildWorker().DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.TotalValue.Should().Be(300_000L, "rebuild replaced the stale stub value");
        aum.DirtyAt.Should().BeNull("drain cleared the flag after rebuild");
    }

    [Fact]
    public async Task DrainOnce_DirtyButInsideCooldown_LeavesAlone()
    {
        await SeedHoldings();
        var dirtyAt = DateTime.UtcNow.AddMinutes(-10);
        await using (var seed = FreshContext())
        {
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 999L,
                    DirtyAt = dirtyAt,
                }
            );
            await seed.SaveChangesAsync();
        }

        // Real-world cooldown of 1h — the dirty timestamp is only 10 min old.
        await BuildWorker(cooldown: TimeSpan.FromHours(1)).DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.TotalValue.Should().Be(999L, "still inside the cooldown — no rebuild");
        aum.DirtyAt.Should()
            .NotBeNull()
            .And.Subject.As<DateTime?>()
            .Value.Should()
            .BeCloseTo(dirtyAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DrainOnce_DirtyAtChangedDuringRebuild_LeavesFlagSetForNextTick()
    {
        await SeedHoldings();
        var staleDirtyAt = DateTime.UtcNow.AddHours(-2);
        var newDirtyAt = DateTime.UtcNow;
        await using (var seed = FreshContext())
        {
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 999L,
                    DirtyAt = staleDirtyAt,
                }
            );
            await seed.SaveChangesAsync();
        }

        // Override RebuildQuarterAsync via a derived refresh service: while the
        // "rebuild" is in flight, simulate a new consumer event landing by
        // updating DirtyAt to a different timestamp. The drain's optimistic
        // clear should see DirtyAt no longer matches and leave the row dirty.
        var racingRefresh = new RacingRefreshService(
            BuildScopeFactory(),
            NullLogger<HoldingsAggregateRefreshService>.Instance,
            onRebuild: async ct =>
            {
                await using var mutate = FreshContext();
                var activeClaim = await mutate
                    .Set<AumQuarterlySnapshot>()
                    .Where(s => s.ReportDate == Q4)
                    .Select(s => s.DirtyAt)
                    .SingleAsync(ct);
                activeClaim
                    .Should()
                    .NotBeNull("request paths must see the snapshot as dirty during rebuild")
                    .And.Subject.As<DateTime?>()
                    .Value.Should()
                    .BeAfter(DateTime.UtcNow, "the drain owns a recoverable future-dated lease");
                await mutate
                    .Set<AumQuarterlySnapshot>()
                    .Where(s => s.ReportDate == Q4)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.DirtyAt, newDirtyAt), ct);
            }
        );
        var worker = new TestableDrainWorker(
            BuildScopeFactory(),
            racingRefresh,
            NullLogger<AumSnapshotDrainWorker>.Instance,
            cooldown: TimeSpan.Zero
        );

        await worker.DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.DirtyAt.Should()
            .NotBeNull()
            .And.Subject.As<DateTime?>()
            .Value.Should()
            .BeCloseTo(
                newDirtyAt,
                TimeSpan.FromSeconds(1),
                "drain saw DirtyAt changed and skipped clear"
            );
    }

    [Fact]
    public async Task DrainOnce_LongRebuildRenewsClaimAndClearsDirtyAt()
    {
        var dirtyAt = DateTime.UtcNow.AddHours(-2);
        await using (var seed = FreshContext())
        {
            seed.Add(new AumQuarterlySnapshot { ReportDate = Q4, DirtyAt = dirtyAt });
            await seed.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory();
        var worker = new TestableDrainWorker(
            scopeFactory,
            new DelayedRefreshService(
                scopeFactory,
                NullLogger<HoldingsAggregateRefreshService>.Instance,
                TimeSpan.FromMilliseconds(180)
            ),
            NullLogger<AumSnapshotDrainWorker>.Instance,
            cooldown: TimeSpan.Zero,
            claimLease: TimeSpan.FromMilliseconds(80),
            claimRenewInterval: TimeSpan.FromMilliseconds(15)
        );

        await worker.DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var snapshot = await read.Set<AumQuarterlySnapshot>().SingleAsync();
        snapshot.DirtyAt.Should().BeNull("the renewed lease remained active through completion");
    }

    [Fact]
    public async Task DrainOnce_TransactionalRebuildDoesNotBlockClaimRenewal()
    {
        await SeedHoldings();
        var dirtyAt = DateTime.UtcNow.AddHours(-2);
        await using (var seed = FreshContext())
        {
            seed.Add(new AumQuarterlySnapshot { ReportDate = Q4, DirtyAt = dirtyAt });
            await seed.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory();
        var worker = new TestableDrainWorker(
            scopeFactory,
            new DelayedBeforeCombinedRefreshService(
                scopeFactory,
                NullLogger<HoldingsAggregateRefreshService>.Instance,
                TimeSpan.FromMilliseconds(180)
            ),
            NullLogger<AumSnapshotDrainWorker>.Instance,
            cooldown: TimeSpan.Zero,
            claimLease: TimeSpan.FromMilliseconds(80),
            claimRenewInterval: TimeSpan.FromMilliseconds(15)
        );

        await worker.DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var snapshot = await read.Set<AumQuarterlySnapshot>().SingleAsync();
        snapshot
            .DirtyAt.Should()
            .BeNull("the transaction leaves the lease row unlocked until publication");
    }

    [Fact]
    public async Task DrainOnce_MissingListingGenerationRunsBeforeMaterializedQuarter()
    {
        await SeedHoldings();
        var missingDate = new DateOnly(2025, 3, 31);
        var dirtyAt = DateTime.UtcNow.AddHours(-2);
        await using (var seed = FreshContext())
        {
            var stockId = await seed.Set<CommonStock>().Select(s => s.Id).FirstAsync();
            seed.AddRange(
                new AumQuarterlySnapshot { ReportDate = Q4, DirtyAt = dirtyAt },
                new AumQuarterlySnapshot { ReportDate = missingDate, DirtyAt = dirtyAt },
                new StockQuarterlyListingActivity
                {
                    CommonStockId = stockId,
                    ReportDate = Q4,
                    PriceSeriesTicker = "AAPL",
                    CurrentShares = 1,
                }
            );
            await seed.SaveChangesAsync();
        }

        var rebuildOrder = new List<DateOnly>();
        var scopeFactory = BuildScopeFactory();
        var worker = new TestableDrainWorker(
            scopeFactory,
            new RecordingRefreshService(
                scopeFactory,
                NullLogger<HoldingsAggregateRefreshService>.Instance,
                rebuildOrder
            ),
            NullLogger<AumSnapshotDrainWorker>.Instance,
            cooldown: TimeSpan.Zero
        );

        await worker.DrainOnce(CancellationToken.None);

        rebuildOrder.Should().Equal(missingDate, Q4);
    }

    [Fact]
    public async Task DrainOnce_ExpiredUnrenewedClaimIsRearmedInsteadOfCleared()
    {
        var dirtyAt = DateTime.UtcNow.AddHours(-2);
        await using (var seed = FreshContext())
        {
            seed.Add(new AumQuarterlySnapshot { ReportDate = Q4, DirtyAt = dirtyAt });
            await seed.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory();
        var worker = new TestableDrainWorker(
            scopeFactory,
            new DelayedRefreshService(
                scopeFactory,
                NullLogger<HoldingsAggregateRefreshService>.Instance,
                TimeSpan.FromMilliseconds(80)
            ),
            NullLogger<AumSnapshotDrainWorker>.Instance,
            cooldown: TimeSpan.Zero,
            claimLease: TimeSpan.FromMilliseconds(20),
            claimRenewInterval: TimeSpan.FromSeconds(5)
        );

        await worker.DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var snapshot = await read.Set<AumQuarterlySnapshot>().SingleAsync();
        snapshot
            .DirtyAt.Should()
            .NotBeNull("an expired claim is never cleared")
            .And.Subject.As<DateTime?>()
            .Value.Should()
            .BeCloseTo(dirtyAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DrainOnce_LateRenewalCannotResurrectExpiredClaimAfterEvent()
    {
        await SeedHoldings();
        var dirtyAt = DateTime.UtcNow.AddHours(-2);
        await using (var seed = FreshContext())
        {
            seed.Add(new AumQuarterlySnapshot { ReportDate = Q4, DirtyAt = dirtyAt });
            await seed.SaveChangesAsync();
        }

        var scopeFactory = BuildScopeFactory();
        var racingRefresh = new RacingRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance,
            onRebuild: async ct =>
            {
                // Let the lease expire, then simulate the consumer's keep-the-first-event
                // expression. An expired marker sorts before the new event and therefore stays
                // in place; the drain must not resurrect it when the delayed renewal runs.
                await Task.Delay(TimeSpan.FromMilliseconds(130), ct);
                var incoming = DateTime.UtcNow;
                await using var mutate = FreshContext();
                await mutate
                    .Set<AumQuarterlySnapshot>()
                    .Where(s => s.ReportDate == Q4)
                    .ExecuteUpdateAsync(
                        s =>
                            s.SetProperty(
                                x => x.DirtyAt,
                                x =>
                                    x.DirtyAt == null || x.DirtyAt > incoming ? incoming : x.DirtyAt
                            ),
                        ct
                    );
                await Task.Delay(TimeSpan.FromMilliseconds(150), ct);
            }
        );
        var worker = new TestableDrainWorker(
            scopeFactory,
            racingRefresh,
            NullLogger<AumSnapshotDrainWorker>.Instance,
            cooldown: TimeSpan.Zero,
            claimLease: TimeSpan.FromMilliseconds(100),
            claimRenewInterval: TimeSpan.FromMilliseconds(200)
        );

        await worker.DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var snapshot = await read.Set<AumQuarterlySnapshot>().SingleAsync();
        snapshot
            .DirtyAt.Should()
            .NotBeNull("an expired claim can only be rearmed, never renewed and cleared")
            .And.Subject.As<DateTime?>()
            .Value.Should()
            .BeCloseTo(dirtyAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DrainOnce_MultipleEventsCoalesceToSingleRebuild()
    {
        await SeedHoldings();
        await using (var seed = FreshContext())
        {
            seed.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = Q4,
                    TotalValue = 999L,
                    DirtyAt = DateTime.UtcNow.AddHours(-2),
                }
            );
            await seed.SaveChangesAsync();
        }

        // First drain: rebuilds and clears DirtyAt.
        await BuildWorker().DrainOnce(CancellationToken.None);

        // A second event arrives — consumer marks dirty again with a fresh timestamp.
        await using (var mark = FreshContext())
        {
            await mark.Set<AumQuarterlySnapshot>()
                .Where(s => s.ReportDate == Q4 && s.DirtyAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DirtyAt, DateTime.UtcNow));
        }

        // Second drain (cooldown = zero → rebuild fires again).
        await BuildWorker().DrainOnce(CancellationToken.None);

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.DirtyAt.Should().BeNull();
        aum.TotalValue.Should().Be(300_000L);
    }

    /// <summary>
    /// Drain worker that lets tests override the cooldown so a "two-hour-old"
    /// dirty flag can be drained in a sub-second test.
    /// </summary>
    private sealed class TestableDrainWorker : AumSnapshotDrainWorker
    {
        private readonly TimeSpan _cooldown;
        private readonly TimeSpan? _claimLease;
        private readonly TimeSpan? _claimRenewInterval;

        public TestableDrainWorker(
            IServiceScopeFactory scopeFactory,
            HoldingsAggregateRefreshService refreshService,
            ILogger<AumSnapshotDrainWorker> logger,
            TimeSpan cooldown,
            TimeSpan? claimLease = null,
            TimeSpan? claimRenewInterval = null
        )
            : base(scopeFactory, refreshService, logger)
        {
            _cooldown = cooldown;
            _claimLease = claimLease;
            _claimRenewInterval = claimRenewInterval;
        }

        protected override TimeSpan StartupDelay => TimeSpan.Zero;
        protected override TimeSpan TickInterval => TimeSpan.FromMilliseconds(1);
        protected override TimeSpan Cooldown => _cooldown;
        protected override TimeSpan ClaimLease => _claimLease ?? base.ClaimLease;
        protected override TimeSpan ClaimRenewInterval =>
            _claimRenewInterval ?? base.ClaimRenewInterval;
    }

    private sealed class DelayedRefreshService : HoldingsAggregateRefreshService
    {
        private readonly TimeSpan _delay;

        public DelayedRefreshService(
            IServiceScopeFactory scopeFactory,
            ILogger<HoldingsAggregateRefreshService> logger,
            TimeSpan delay
        )
            : base(scopeFactory, logger)
        {
            _delay = delay;
        }

        public override Task RebuildQuarterAsync(
            DateOnly reportDate,
            CancellationToken cancellationToken
        ) => Task.Delay(_delay, cancellationToken);
    }

    private sealed class DelayedBeforeCombinedRefreshService : HoldingsAggregateRefreshService
    {
        private readonly TimeSpan _delay;

        public DelayedBeforeCombinedRefreshService(
            IServiceScopeFactory scopeFactory,
            ILogger<HoldingsAggregateRefreshService> logger,
            TimeSpan delay
        )
            : base(scopeFactory, logger)
        {
            _delay = delay;
        }

        protected override Task BeforeCombinedLaneRefresh(
            DateOnly reportDate,
            CancellationToken cancellationToken
        ) => Task.Delay(_delay, cancellationToken);
    }

    private sealed class RecordingRefreshService : HoldingsAggregateRefreshService
    {
        private readonly List<DateOnly> _rebuildOrder;

        public RecordingRefreshService(
            IServiceScopeFactory scopeFactory,
            ILogger<HoldingsAggregateRefreshService> logger,
            List<DateOnly> rebuildOrder
        )
            : base(scopeFactory, logger)
        {
            _rebuildOrder = rebuildOrder;
        }

        public override Task RebuildQuarterAsync(
            DateOnly reportDate,
            CancellationToken cancellationToken
        )
        {
            _rebuildOrder.Add(reportDate);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Refresh-service double that, between rebuilding the snapshot and
    /// returning, invokes the supplied callback so a test can simulate a
    /// concurrent consumer event arriving mid-rebuild.
    /// </summary>
    private sealed class RacingRefreshService : HoldingsAggregateRefreshService
    {
        private readonly Func<CancellationToken, Task> _onRebuild;

        public RacingRefreshService(
            IServiceScopeFactory scopeFactory,
            ILogger<HoldingsAggregateRefreshService> logger,
            Func<CancellationToken, Task> onRebuild
        )
            : base(scopeFactory, logger)
        {
            _onRebuild = onRebuild;
        }

        public override async Task RebuildQuarterAsync(
            DateOnly reportDate,
            CancellationToken cancellationToken
        )
        {
            await base.RebuildQuarterAsync(reportDate, cancellationToken);
            await _onRebuild(cancellationToken);
        }
    }

    private async Task SeedHoldings()
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
        var msft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft",
            Cik = "C0000789019",
            IndustryId = industry.Id,
        };
        seed.AddRange(aapl, msft);
        var holder = new InstitutionalHolder { Cik = "H001", Name = "Holder H001" };
        seed.Add(holder);
        await seed.SaveChangesAsync();
        seed.AddRange(
            MakeHolding(aapl, holder, Q4, 100_000, "acc-q4"),
            MakeHolding(msft, holder, Q4, 200_000, "acc-q4")
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

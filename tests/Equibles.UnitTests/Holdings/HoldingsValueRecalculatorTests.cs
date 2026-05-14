using Equibles.CommonStocks.Data;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// HoldingsValueRecalculator is the second-chance resolver for InstitutionalHolding rows
/// imported without a Yahoo close price (ValuePending=true). The HoldingsScraperWorker
/// invokes it at the tail of every 24-hour cycle, so its behaviour decides whether
/// stale-priced 13F rows ever heal or sit pending forever. The class is heavily
/// DI-coupled (per-call CreateScope, EF DbContext per scope, IStockPriceProvider).
/// Tests use the EF in-memory provider behind a real IServiceScopeFactory, exactly the
/// scaffold DocumentScraperTests uses for the same shape.
/// </summary>
public class HoldingsValueRecalculatorTests
{
    [Fact]
    public async Task Recalculate_NoPendingHoldings_DoesNotInvokePriceProvider()
    {
        // Pin the early-return on the empty-pending fast path. The worker invokes
        // Recalculate at the end of every 24-hour cycle whether or not any
        // ValuePending rows exist — most cycles will see zero pending pairs after
        // the first run, so this branch fires on every steady-state cycle.
        // A regression that dropped the `if (pendingPairs.Count == 0) return;`
        // guard would forward an empty request list to IStockPriceProvider on
        // every cycle. Today that's a wasted Yahoo round-trip; tomorrow when the
        // provider switches to a paid Alpha-Vantage / Polygon backend it's a wasted
        // quota call per worker per day. Pin both signals: no provider call AND no
        // observable change to existing non-pending holdings.
        var harness = new Harness();
        await using var db = harness.CreateDbContext();
        SeedHolding(db, valuePending: false, shares: 100, value: 5_000);

        await harness.BuildRecalculator(db).Recalculate(CancellationToken.None);

        await harness.PriceProvider.DidNotReceiveWithAnyArgs().GetClosingPrices(default, default);
        var only = await db.Set<InstitutionalHolding>().AsNoTracking().SingleAsync();
        only.Value.Should().Be(5_000);
        only.ValuePending.Should().BeFalse();
    }

    [Fact]
    public async Task Recalculate_PriceFoundForPendingHolding_SetsValueAndClearsPending()
    {
        // Pin the happy-path resolution flow — the whole reason this class exists.
        // 13F filings land on a quarterly cadence and Yahoo's adjusted-close
        // history sometimes trails the SEC release by a day or two on illiquid
        // tickers. Holdings imported with ValuePending=true rely on this method
        // to recompute Value once Yahoo catches up, AND to propagate the same
        // close price into every owned HoldingManagerEntry (multi-manager
        // filings). A regression that updated holding.Value but forgot the
        // entry.Value loop would silently leave per-manager attribution rows
        // worth $0 — the parent holding would look correct on the dashboard
        // but the manager-level breakdown would be wrong, an asymmetric failure
        // mode invisible to anyone who only checks the parent. Pin all four
        // observables in one go: holding.Value, holding.ValuePending,
        // entry.Value, and that the resolved pair is excluded from the
        // unresolved-retry pass (no ValueRetryCount bump).
        var harness = new Harness();
        await using var db = harness.CreateDbContext();
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 9, 30);
        SeedHolding(
            db,
            valuePending: true,
            shares: 1_000,
            value: 0,
            commonStockId: stockId,
            reportDate: reportDate,
            managerEntries: [new HoldingManagerEntry { Shares = 600, Value = 0 }]
        );

        harness
            .PriceProvider.GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<(Guid CommonStockId, DateOnly Date), decimal>
                {
                    [(stockId, reportDate)] = 12.50m,
                }
            );

        await harness.BuildRecalculator(db).Recalculate(CancellationToken.None);

        var only = await db.Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .AsNoTracking()
            .SingleAsync();
        only.Value.Should().Be(12_500); // 1000 shares × $12.50
        only.ValuePending.Should().BeFalse();
        only.ValueRetryCount.Should().Be(0); // never entered the unresolved-retry pass
        only.ManagerEntries.Should().ContainSingle().Which.Value.Should().Be(7_500); // 600 shares × $12.50
    }

    [Fact]
    public async Task Recalculate_UnresolvedAndAnchorInsideBackoffWindow_DoesNotBumpRetryCount()
    {
        // Pin the backoff-window respect on the unresolved-retry pass. The
        // RetryDelays schedule (1d, 7d, 30d) exists because Yahoo's
        // adjusted-close history backfills on an unpredictable lag — pounding
        // it daily for a price that simply isn't published yet is wasted quota
        // AND wasted DB writes (every retry increments ValueRetryCount + sets
        // ValueLastRetryAt, both persisted). A regression that dropped the
        // `if (anchor.Add(delay) > now) continue;` guard would re-attempt every
        // pending pair on every cycle, exhausting MaxRetries=3 in 3 days and
        // permanently abandoning rows that would have resolved naturally on
        // day 7 or day 30. Pin the boundary: a freshly-imported holding
        // (CreationTime = now-12h, ValueRetryCount=0 so delay=1 day) must NOT
        // be retried until 24 hours after creation. The sibling resolved-path
        // pin already covers "happens at all"; this one defends the cadence.
        var harness = new Harness();
        await using var db = harness.CreateDbContext();
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 9, 30);
        var creationTime = DateTime.UtcNow.AddHours(-12); // inside 1-day delay window
        SeedHolding(
            db,
            valuePending: true,
            shares: 500,
            value: 0,
            commonStockId: stockId,
            reportDate: reportDate,
            creationTime: creationTime
        );

        harness
            .PriceProvider.GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<(Guid CommonStockId, DateOnly Date), decimal>());

        await harness.BuildRecalculator(db).Recalculate(CancellationToken.None);

        var only = await db.Set<InstitutionalHolding>().AsNoTracking().SingleAsync();
        only.ValuePending.Should().BeTrue();
        only.ValueRetryCount.Should().Be(0);
        only.ValueLastRetryAt.Should().BeNull();
        only.Value.Should().Be(0);
    }

    [Fact]
    public async Task Recalculate_UnresolvedPastAllBackoffs_GivesUpAndClearsPending()
    {
        // Pin the give-up condition: ValueRetryCount > MaxRetries(=3) → ValuePending=false.
        // This is the terminal state of the retry ladder — without it, holdings whose
        // Yahoo price NEVER lands (delisted ticker, name change with broken CUSIP map,
        // OTC issue without a Yahoo feed) would loop ValuePending=true forever, dragging
        // every nightly Recalculate run through a request set that grows unboundedly
        // across quarters. The 30-day delay on RetryDelays[2] caps the worst-case wait at
        // ~38 days (1 + 7 + 30) before the row resolves to "give up with Value=0", which
        // is what the dashboards then surface as "price unavailable" rather than
        // pretending the position is worth $0. The asymmetric risk a regression could
        // introduce: flipping `>` to `>=` in the give-up check would give up one cycle
        // EARLY (ValueRetryCount=3 ≥ 3 → give up at retry #3, not retry #4),
        // truncating the 30-day final wait. Flipping it to `<` would never give up,
        // turning the table into a permanent leak. Pin the literal boundary by
        // pre-staging ValueRetryCount=3 with ValueLastRetryAt far enough in the past
        // (40 days) to satisfy the 30-day delay — the next Recalculate must observe
        // ValueRetryCount=4 AND ValuePending=false in the SAME pass.
        var harness = new Harness();
        await using var db = harness.CreateDbContext();
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 9, 30);
        SeedHolding(
            db,
            valuePending: true,
            shares: 250,
            value: 0,
            commonStockId: stockId,
            reportDate: reportDate,
            valueRetryCount: 3,
            valueLastRetryAt: DateTime.UtcNow.AddDays(-40)
        );

        harness
            .PriceProvider.GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<(Guid CommonStockId, DateOnly Date), decimal>());

        await harness.BuildRecalculator(db).Recalculate(CancellationToken.None);

        var only = await db.Set<InstitutionalHolding>().AsNoTracking().SingleAsync();
        only.ValueRetryCount.Should().Be(4);
        only.ValuePending.Should().BeFalse();
        only.Value.Should().Be(0);
    }

    private static void SeedHolding(
        EquiblesDbContext db,
        bool valuePending,
        long shares,
        long value,
        Guid? commonStockId = null,
        DateOnly? reportDate = null,
        DateTime? creationTime = null,
        int valueRetryCount = 0,
        DateTime? valueLastRetryAt = null,
        List<HoldingManagerEntry> managerEntries = null
    )
    {
        db.Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = Guid.NewGuid(),
                    CommonStockId = commonStockId ?? Guid.NewGuid(),
                    FilingDate = new DateOnly(2024, 11, 14),
                    ReportDate = reportDate ?? new DateOnly(2024, 9, 30),
                    Value = value,
                    Shares = shares,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    Cusip = "037833100",
                    AccessionNumber = $"0000000000-24-{Guid.NewGuid().ToString("N")[..6]}",
                    ValuePending = valuePending,
                    ValueRetryCount = valueRetryCount,
                    ValueLastRetryAt = valueLastRetryAt,
                    CreationTime = creationTime ?? DateTime.UtcNow,
                    ManagerEntries = managerEntries ?? [],
                }
            );
        db.SaveChanges();
    }

    private sealed class Harness
    {
        public IStockPriceProvider PriceProvider { get; } = Substitute.For<IStockPriceProvider>();
        public IServiceScopeFactory ScopeFactory { get; private set; }

        public EquiblesDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<EquiblesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options;
            var modules = new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
            };
            var db = new EquiblesDbContext(options, modules);
            db.Database.EnsureCreated();
            return db;
        }

        public HoldingsValueRecalculator BuildRecalculator(EquiblesDbContext db)
        {
            // Register the in-memory context as a singleton INSTANCE so MS.DI doesn't
            // dispose it across the many short-lived scopes Recalculate creates inside
            // one call. Mirrors the DocumentScraperTests harness for the same reason.
            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton(PriceProvider);
            var provider = services.BuildServiceProvider();
            ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new HoldingsValueRecalculator(
                ScopeFactory,
                PriceProvider,
                Substitute.For<ILogger<HoldingsValueRecalculator>>()
            );
        }
    }
}

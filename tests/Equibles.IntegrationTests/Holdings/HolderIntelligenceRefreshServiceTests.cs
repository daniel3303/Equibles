using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for <see cref="HolderIntelligenceRefreshService"/> and
/// <see cref="HolderIntelligenceDrainWorker"/> on Postgres: metrics come from each filer's own
/// Form 13F book, the prior quarter is restated across splits, presence bits follow the filed
/// books, and the drain covers every closed quarter from the corpus start.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderIntelligenceRefreshServiceTests : IAsyncLifetime
{
    private static readonly DateOnly BeforeCorpus = new(2023, 12, 31);
    private static readonly DateOnly Q1 = new(2024, 3, 31);
    private static readonly DateOnly Q2 = new(2024, 6, 30);
    private static readonly DateOnly Q3 = new(2024, 9, 30);
    private static readonly DateOnly Today = new(2024, 11, 30);

    private readonly ParadeDbFixture _fixture;
    private ServiceProvider _provider;
    private EquityIssuer _apple;
    private EquityIssuer _microsoft;
    private InstitutionalHolder _holder;

    public HolderIntelligenceRefreshServiceTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _fixture.CreateDbContext());
        services.AddSingleton(
            new HoldingsCorpusCoverage(
                Options.Create(new WorkerOptions { MinSyncDate = new DateTime(2024, 1, 1) })
            )
        );
        services.AddScoped<HolderIntelligenceRefreshService>();
        _provider = services.BuildServiceProvider();

        await using var seed = _fixture.CreateDbContext();
        _apple = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "AAPL", Name: "Apple");
        _microsoft = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MSFT",
            Name: "Microsoft"
        );
        _holder = new InstitutionalHolder { Cik = "0000000001", Name = "Filer One" };
        seed.AddRange(_apple, _microsoft, _holder);
        await seed.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task RebuildQuarter_MeasuresOnlyTheFilersOwnForm13FBook()
    {
        var blockHolder = new InstitutionalHolder { Cik = "0000000002", Name = "Block Holder" };
        await Seed(seed =>
        {
            seed.Add(blockHolder);
            seed.AddRange(
                Filing(_holder, BeforeCorpus),
                Holding(_apple, _holder, BeforeCorpus, 100, 9_000),
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                Holding(_microsoft, _holder, Q1, 50, 5_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 150, 18_000),
                Holding(_apple, _holder, Q2, 10, 700, optionType: OptionType.Put),
                Filing(_holder, Q2, FilingType.Schedule13G),
                Holding(_microsoft, _holder, Q2, 1_000, 100_000, FilingType.Schedule13G),
                Filing(blockHolder, Q2, FilingType.Schedule13D),
                Holding(_apple, blockHolder, Q2, 5_000, 600_000, FilingType.Schedule13D)
            );
        });

        (await Rebuild(Q1)).Should().BeTrue();
        (await Rebuild(Q2)).Should().BeTrue();

        await using var read = _fixture.CreateDbContext();
        var metrics = await read.Set<HolderQuarterlyMetrics>().ToListAsync();
        metrics.Should().OnlyContain(row => row.InstitutionalHolderId == _holder.Id);
        var first = metrics.Single(row => row.ReportDate == Q1);
        first.PreviousEquityAum.Should().BeNull("the quarter before the corpus is no baseline");
        first.NewCount.Should().BeNull();
        var second = metrics.Single(row => row.ReportDate == Q2);
        second.EquityAum.Should().Be(18_000);
        second.PreviousEquityAum.Should().Be(15_000);
        second.StockCount.Should().Be(1);
        second.IncreasedCount.Should().Be(1);
        second.ExitCount.Should().Be(1);
        second.BuyValue.Should().Be(6_000m, "50 shares at the 120 quarter-end price");
        second.SellValue.Should().Be(5_000m, "the exit at the prior quarter's 100");
        second
            .Turnover.Should()
            .BeApproximately(5_000m / 16_500m, 0.000001m, "stored at six decimals");
        second.PutValue.Should().Be(700);
        second
            .AggregateActiveShare.Should()
            .Be(0m, "the benchmark is the 13F aggregate, which here is this book alone");
        second.CalculationVersion.Should().Be(HolderIntelligenceRefreshService.CalculationVersion);
    }

    [Fact]
    public async Task RebuildQuarter_RestatesThePriorQuarterAcrossASplit()
    {
        await Seed(seed =>
            seed.AddRange(
                Split(_apple, new DateOnly(2024, 5, 15), "AAPL"),
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 200, 10_000)
            )
        );

        await Rebuild(Q2);

        var metrics = await Metrics(Q2);
        metrics.IncreasedCount.Should().Be(0, "100 pre-split shares are 200 after a 2-for-1");
        metrics.UnpricedTradeCount.Should().Be(0);
        metrics.Turnover.Should().Be(0m);
    }

    [Fact]
    public async Task RebuildQuarter_AnUnattributedSplitLeavesTheTradeUnpriced()
    {
        await Seed(seed =>
            seed.AddRange(
                Split(_apple, new DateOnly(2024, 5, 15), priceSeriesTicker: null),
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 200, 10_000)
            )
        );

        await Rebuild(Q2);

        var metrics = await Metrics(Q2);
        metrics.IncreasedCount.Should().Be(0);
        metrics.UnpricedTradeCount.Should().Be(1);
    }

    [Fact]
    public async Task RebuildQuarter_KeepsPresenceBitsAndTenureInStepWithTheBooks()
    {
        await Seed(seed =>
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                Holding(_microsoft, _holder, Q1, 50, 5_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 100, 12_000)
            )
        );

        await Rebuild(Q1);
        await Rebuild(Q2);

        var bits = Bits(Q1, Q2);
        (await Masks())
            .Should()
            .BeEquivalentTo(
                new Dictionary<Guid, long> { [_apple.Id] = bits, [_microsoft.Id] = Bits(Q1) }
            );
        var metrics = await Metrics(Q2);
        metrics.ValueWeightedTenureQuarters.Should().Be(2m);
        metrics.CensoredValueShare.Should().Be(1m, "the run reaches the corpus start");

        // The Q2 filing is withdrawn: its bit is cleared and its metrics row is gone.
        await using (var mutate = _fixture.CreateDbContext())
        {
            await mutate
                .Set<InstitutionalHolding>()
                .Where(holding => holding.ReportDate == Q2)
                .ExecuteDeleteAsync();
            await mutate
                .Set<InstitutionalFiling>()
                .Where(filing => filing.ReportDate == Q2)
                .ExecuteDeleteAsync();
        }
        await Rebuild(Q2);

        (await Masks())
            .Should()
            .BeEquivalentTo(
                new Dictionary<Guid, long> { [_apple.Id] = Bits(Q1), [_microsoft.Id] = Bits(Q1) }
            );
        await using var read = _fixture.CreateDbContext();
        (await read.Set<HolderQuarterlyMetrics>().AnyAsync(row => row.ReportDate == Q2))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task RebuildQuarter_AChangedBitDirtiesEveryLaterQuarter()
    {
        await Seed(seed =>
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                new HolderIntelligenceQuarterState { ReportDate = Q1 },
                new HolderIntelligenceQuarterState { ReportDate = Q2 },
                new HolderIntelligenceQuarterState { ReportDate = Q3 }
            )
        );

        await Rebuild(Q1);

        await using (var read = _fixture.CreateDbContext())
        {
            var states = await read.Set<HolderIntelligenceQuarterState>()
                .ToDictionaryAsync(state => state.ReportDate, state => state.DirtyAt);
            states[Q1]
                .Should()
                .BeNull("the drain, not the rebuild, clears the quarter's own stamp");
            states[Q2]
                .Should()
                .BeBefore(DateTime.UtcNow - HolderIntelligenceRefreshService.Cooldown);
            states[Q3]
                .Should()
                .BeBefore(DateTime.UtcNow - HolderIntelligenceRefreshService.Cooldown);
        }

        await using (var reset = _fixture.CreateDbContext())
            await reset
                .Set<HolderIntelligenceQuarterState>()
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(state => state.DirtyAt, (DateTime?)null)
                );
        await Rebuild(Q1);

        await using var after = _fixture.CreateDbContext();
        (await after.Set<HolderIntelligenceQuarterState>().AnyAsync(state => state.DirtyAt != null))
            .Should()
            .BeFalse("an unchanged rebuild leaves later quarters alone");
    }

    [Fact]
    public async Task RebuildQuarter_AFailedPublishStillLeavesLaterQuartersStamped()
    {
        await Seed(seed =>
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                new HolderIntelligenceQuarterState { ReportDate = Q2 },
                new HolderIntelligenceQuarterState { ReportDate = Q3 }
            )
        );

        await using (var context = _fixture.CreateDbContext())
        {
            var failing = new FailingPublishRefreshService(
                context,
                _provider.GetRequiredService<HoldingsCorpusCoverage>()
            );
            var rebuild = () => failing.RebuildQuarter(Q1, Today, CancellationToken.None);
            await rebuild.Should().ThrowAsync<InvalidOperationException>();
        }

        (await Masks()).Should().ContainKey(_apple.Id, "the presence bit committed");
        await using var read = _fixture.CreateDbContext();
        (await read.Set<HolderIntelligenceQuarterState>().AllAsync(state => state.DirtyAt != null))
            .Should()
            .BeTrue("the bit and the stamp of the quarters that read it commit together");
        (await read.Set<HolderQuarterlyMetrics>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RebuildQuarter_AFailedPublishAfterAWithdrawnFilingStillStampsLaterQuarters()
    {
        await Seed(seed =>
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                new HolderIntelligenceQuarterState { ReportDate = Q2 }
            )
        );
        await Rebuild(Q1);
        await using (var mutate = _fixture.CreateDbContext())
        {
            await mutate
                .Set<HolderIntelligenceQuarterState>()
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(state => state.DirtyAt, (DateTime?)null)
                );
            await mutate.Set<InstitutionalHolding>().ExecuteDeleteAsync();
            await mutate.Set<InstitutionalFiling>().ExecuteDeleteAsync();
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var failing = new FailingPublishRefreshService(
                context,
                _provider.GetRequiredService<HoldingsCorpusCoverage>()
            );
            var rebuild = () => failing.RebuildQuarter(Q1, Today, CancellationToken.None);
            await rebuild.Should().ThrowAsync<InvalidOperationException>();
        }

        (await Masks())[_apple.Id].Should().Be(0, "the withdrawn filing's bit is cleared");
        await using var read = _fixture.CreateDbContext();
        (await read.Set<HolderIntelligenceQuarterState>().SingleAsync())
            .DirtyAt.Should()
            .NotBeNull("the clear and the stamp commit together");
    }

    [Fact]
    public async Task RebuildQuarter_ATurnoverChangeDirtiesTheThreeQuartersThatSumIt()
    {
        var q4 = new DateOnly(2024, 12, 31);
        var q5 = new DateOnly(2025, 3, 31);
        var q6 = new DateOnly(2025, 6, 30);
        await Seed(seed =>
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                Holding(_microsoft, _holder, Q1, 100, 10_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 150, 18_000),
                Holding(_microsoft, _holder, Q2, 50, 6_000),
                new HolderIntelligenceQuarterState { ReportDate = Q3 },
                new HolderIntelligenceQuarterState { ReportDate = q4 },
                new HolderIntelligenceQuarterState { ReportDate = q5 },
                new HolderIntelligenceQuarterState { ReportDate = q6 }
            )
        );
        await Rebuild(Q1);
        await Rebuild(Q2);
        await using (var mutate = _fixture.CreateDbContext())
        {
            await mutate
                .Set<HolderIntelligenceQuarterState>()
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(state => state.DirtyAt, (DateTime?)null)
                );
            // An amendment to Q1 changes Q2's trades but not which securities are held.
            await mutate
                .Set<InstitutionalHolding>()
                .Where(holding =>
                    holding.ReportDate == Q1 && holding.EquityIssuerId == _microsoft.Id
                )
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(h => h.Shares, 80).SetProperty(h => h.Value, 8_000)
                );
        }

        await Rebuild(Q2);

        await using var read = _fixture.CreateDbContext();
        var states = await read.Set<HolderIntelligenceQuarterState>()
            .ToDictionaryAsync(state => state.ReportDate, state => state.DirtyAt);
        states[Q3].Should().NotBeNull();
        states[q4].Should().NotBeNull();
        states[q5].Should().NotBeNull();
        states[q6].Should().BeNull("only three later quarters sum this turnover");
    }

    [Fact]
    public async Task RebuildQuarter_KeepsSiblingShareClassesApart()
    {
        var berkshire = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BRKA",
            Name: "Berkshire",
            SecondaryTickers: ["BRKB"]
        );
        await Seed(seed => seed.Add(berkshire));
        await Seed(seed =>
            seed.AddRange(
                Split(berkshire, new DateOnly(2024, 5, 15), "BRKA"),
                Filing(_holder, Q1),
                Holding(berkshire, _holder, Q1, 100, 10_000),
                Holding(berkshire, _holder, Q1, 100, 1_000, listedTicker: "BRKB"),
                Filing(_holder, Q2),
                Holding(berkshire, _holder, Q2, 200, 10_000),
                Holding(berkshire, _holder, Q2, 100, 1_100, listedTicker: "BRKB")
            )
        );

        await Rebuild(Q1);
        await Rebuild(Q2);

        var metrics = await Metrics(Q2);
        metrics.IncreasedCount.Should().Be(0, "the split restates only the class it names");
        metrics.ReducedCount.Should().Be(0);
        metrics.StockCount.Should().Be(1, "both classes are one issuer");
        await using var read = _fixture.CreateDbContext();
        var rows = await read.Set<HolderStockPresence>()
            .Where(row => row.EquityIssuerId == berkshire.Id)
            .Select(row => new { row.ListedTicker, row.HeldMask })
            .ToListAsync();
        rows.Should()
            .BeEquivalentTo([
                new { ListedTicker = (string)null, HeldMask = Bits(Q1, Q2) },
                new { ListedTicker = "BRKB", HeldMask = Bits(Q1, Q2) },
            ]);
    }

    [Fact]
    public async Task RebuildQuarter_RefusesAnOpenOrUncoveredQuarter()
    {
        (await Rebuild(new DateOnly(2024, 12, 31))).Should().BeFalse("its filing window is open");
        (await Rebuild(BeforeCorpus)).Should().BeFalse("it precedes the corpus");
        (await Rebuild(new DateOnly(2024, 5, 31))).Should().BeFalse("it is not a quarter end");
    }

    [Fact]
    public async Task DrainOnce_ComputesEveryClosedQuarterFromTheCorpusStart()
    {
        var stamped = DateTime.UtcNow.AddDays(-1);
        await Seed(seed =>
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                Filing(_holder, Q3),
                Holding(_apple, _holder, Q3, 100, 12_000),
                new HolderIntelligenceQuarterState { ReportDate = BeforeCorpus, DirtyAt = stamped }
            )
        );

        await Drain().DrainOnce(Today, CancellationToken.None);

        await using var read = _fixture.CreateDbContext();
        var states = await read.Set<HolderIntelligenceQuarterState>()
            .OrderBy(state => state.ReportDate)
            .ToListAsync();
        states.Select(state => state.ReportDate).Should().Equal(BeforeCorpus, Q1, Q2, Q3);
        states[0].DirtyAt.Should().Be(stamped, "a quarter before the corpus is never drained");
        states
            .Skip(1)
            .Should()
            .OnlyContain(state =>
                state.DirtyAt == null
                && state.ComputedAt != null
                && state.CalculationVersion == HolderIntelligenceRefreshService.CalculationVersion
            );
        var metrics = await read.Set<HolderQuarterlyMetrics>().ToListAsync();
        metrics.Select(row => row.ReportDate).Should().BeEquivalentTo([Q1, Q3]);
        var latest = metrics.Single(row => row.ReportDate == Q3);
        latest.ValueWeightedTenureQuarters.Should().Be(3m, "the unfiled Q2 is bridged");
        latest.PreviousEquityAum.Should().BeNull("no 13F was filed for Q2");
    }

    [Fact]
    public async Task DrainOnce_AnOlderCalculationVersionIsComputedAgain()
    {
        await Seed(seed =>
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 10_000),
                new HolderIntelligenceQuarterState
                {
                    ReportDate = Q1,
                    ComputedAt = DateTime.UtcNow.AddDays(-30),
                    CalculationVersion = HolderIntelligenceRefreshService.CalculationVersion - 1,
                }
            )
        );

        await Drain().DrainOnce(Today, CancellationToken.None);

        await using var read = _fixture.CreateDbContext();
        var state = await read.Set<HolderIntelligenceQuarterState>()
            .SingleAsync(s => s.ReportDate == Q1);
        state.CalculationVersion.Should().Be(HolderIntelligenceRefreshService.CalculationVersion);
        state.ComputedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
        (await read.Set<HolderQuarterlyMetrics>().CountAsync(row => row.ReportDate == Q1))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task DrainOnce_AQuarterStampedDuringTheRebuildStaysDirty()
    {
        await Seed(seed =>
            seed.AddRange(Filing(_holder, Q1), Holding(_apple, _holder, Q1, 100, 10_000))
        );
        var restamp = DateTime.UtcNow;
        var worker = new TestDrainWorker(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            beforeRebuild: async reportDate =>
            {
                if (reportDate != Q1)
                    return;
                await using var mutate = _fixture.CreateDbContext();
                await mutate
                    .Set<HolderIntelligenceQuarterState>()
                    .Where(state => state.ReportDate == Q1)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.DirtyAt, restamp));
            }
        );

        await worker.DrainOnce(Today, CancellationToken.None);

        await using var read = _fixture.CreateDbContext();
        var state = await read.Set<HolderIntelligenceQuarterState>()
            .SingleAsync(s => s.ReportDate == Q1);
        state.DirtyAt.Should().BeCloseTo(restamp, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task DrainOnce_AFailedRebuildCountsTheFailureAndStampsAgain()
    {
        await Seed(seed =>
            seed.AddRange(Filing(_holder, Q1), Holding(_apple, _holder, Q1, 100, 10_000))
        );
        var before = DateTime.UtcNow.AddSeconds(-1);
        var worker = new TestDrainWorker(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            beforeRebuild: reportDate =>
                reportDate == Q1
                    ? Task.FromException(new InvalidOperationException("boom"))
                    : Task.CompletedTask
        );

        await worker.DrainOnce(Today, CancellationToken.None);

        await using var read = _fixture.CreateDbContext();
        var state = await read.Set<HolderIntelligenceQuarterState>()
            .SingleAsync(s => s.ReportDate == Q1);
        state.FailureCount.Should().Be(1);
        state.LastError.Should().Be("boom");
        state.DirtyAt.Should().BeAfter(before);
        state.ComputedAt.Should().BeNull();
    }

    [Fact]
    public async Task BaseRebuild_StampsTheQuarterAndTheNextOne()
    {
        await Seed(seed =>
            seed.AddRange(Filing(_holder, Q1), Holding(_apple, _holder, Q1, 100, 10_000))
        );
        var before = DateTime.UtcNow.AddSeconds(-1);

        await new HoldingsAggregateRefreshService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HoldingsAggregateRefreshService>.Instance
        ).RebuildQuarterAsync(Q1, CancellationToken.None);

        await using var read = _fixture.CreateDbContext();
        var states = await read.Set<HolderIntelligenceQuarterState>()
            .ToDictionaryAsync(state => state.ReportDate, state => state.DirtyAt);
        states.Keys.Should().BeEquivalentTo([Q1, Q2]);
        states.Values.Should().OnlyContain(stamp => stamp > before);
    }

    private async Task<bool> Rebuild(DateOnly reportDate)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<HolderIntelligenceRefreshService>()
            .RebuildQuarter(reportDate, Today, CancellationToken.None);
    }

    private TestDrainWorker Drain() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), beforeRebuild: null);

    private async Task<HolderQuarterlyMetrics> Metrics(DateOnly reportDate)
    {
        await using var read = _fixture.CreateDbContext();
        return await read.Set<HolderQuarterlyMetrics>()
            .SingleAsync(row =>
                row.InstitutionalHolderId == _holder.Id && row.ReportDate == reportDate
            );
    }

    private async Task<Dictionary<Guid, long>> Masks()
    {
        await using var read = _fixture.CreateDbContext();
        return await read.Set<HolderStockPresence>()
            .Where(row => row.InstitutionalHolderId == _holder.Id)
            .ToDictionaryAsync(row => row.EquityIssuerId, row => row.HeldMask);
    }

    private async Task Seed(Action<EquiblesFinancialDbContext> add)
    {
        await using var seed = _fixture.CreateDbContext();
        add(seed);
        await seed.SaveChangesAsync();
    }

    private static long Bits(params DateOnly[] quarters) =>
        quarters.Aggregate(
            0L,
            (mask, quarter) =>
                HolderTenureCalculator.Set(mask, HolderTenureCalculator.QuarterIndex(quarter).Value)
        );

    private static InstitutionalFiling Filing(
        InstitutionalHolder holder,
        DateOnly reportDate,
        FilingType type = FilingType.Form13F
    ) =>
        new()
        {
            AccessionNumber = $"{holder.Cik}-{reportDate:yyyyMMdd}-{type}",
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(40),
            ReportDate = reportDate,
            FilingType = type,
        };

    private static InstitutionalHolding Holding(
        EquityIssuer issuer,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value,
        FilingType type = FilingType.Form13F,
        OptionType? optionType = null,
        string listedTicker = null
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            ListedTicker = listedTicker,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(40),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            OptionType = optionType,
            FilingType = type,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"{holder.Cik}-{reportDate:yyyyMMdd}-{type}",
            Cusip = (listedTicker ?? issuer.Presentation.Listing.Ticker).PadRight(9, '0'),
        };

    private static StockSplit Split(
        EquityIssuer issuer,
        DateOnly effective,
        string priceSeriesTicker
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            EquityListingId =
                priceSeriesTicker == null ? null : issuer.Presentation.EquityListingId,
            PriceSeriesTicker = priceSeriesTicker,
            EffectiveDate = effective,
            Numerator = 2m,
            Denominator = 1m,
            Source = StockSplitSource.External,
        };

    private sealed class FailingPublishRefreshService(
        EquiblesFinancialDbContext dbContext,
        HoldingsCorpusCoverage corpusCoverage
    )
        : HolderIntelligenceRefreshService(
            dbContext,
            corpusCoverage,
            NullLogger<HolderIntelligenceRefreshService>.Instance
        )
    {
        protected override Task BeforePublish(DateOnly reportDate) =>
            throw new InvalidOperationException("publish failed");
    }

    private sealed class TestDrainWorker : HolderIntelligenceDrainWorker
    {
        private readonly Func<DateOnly, Task> _beforeRebuild;

        public TestDrainWorker(
            IServiceScopeFactory scopeFactory,
            Func<DateOnly, Task> beforeRebuild
        )
            : base(scopeFactory, NullLogger<HolderIntelligenceDrainWorker>.Instance) =>
            _beforeRebuild = beforeRebuild;

        protected override TimeSpan Cooldown => TimeSpan.Zero;

        protected override Task BeforeRebuild(DateOnly reportDate) =>
            _beforeRebuild?.Invoke(reportDate) ?? Task.CompletedTask;
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.HostedService.Services.Benchmarks;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<EquityIssuerRepository>();
        services.AddHolderBenchmarks();
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
    public async Task RebuildQuarter_StoresTheCombinedBookRowAsTheAggregateActiveShare()
    {
        var other = new InstitutionalHolder { Cik = "0000000003", Name = "Filer Three" };
        await Seed(seed =>
        {
            seed.Add(other);
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 30_000),
                Holding(_microsoft, _holder, Q1, 100, 10_000),
                Filing(other, Q1),
                Holding(_microsoft, other, Q1, 100, 60_000)
            );
        });

        (await Rebuild(Q1)).Should().BeTrue();

        await using var read = _fixture.CreateDbContext();
        var metrics = await read.Set<HolderQuarterlyMetrics>().ToListAsync();
        var rows = await read.Set<HolderQuarterlyBenchmarkActiveShare>().ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(row => row.BenchmarkKey == "13f-aggregate");
        foreach (var filer in metrics)
        {
            var row = rows.Single(row => row.InstitutionalHolderId == filer.InstitutionalHolderId);
            row.ActiveShare.Should().Be(filer.AggregateActiveShare.Value);
            row.BenchmarkAsOf.Should().Be(Q1);
            row.Coverage.Should().Be(1m);
            filer.ClosestBenchmark.Should().Be("13f-aggregate");
            filer.ClosestActiveShare.Should().Be(filer.AggregateActiveShare);
            filer.ClosestBenchmarkAsOf.Should().Be(Q1);
        }
        // Apple is 30% of the 100,000 combined book; this filer holds 75% Apple, 25% Microsoft.
        var mine = metrics.Single(row => row.InstitutionalHolderId == _holder.Id);
        mine.AggregateActiveShare.Should().Be(0.45m, "half of |0.75 - 0.30| + |0.25 - 0.70|");
        mine.SectorActiveShare.Should().Be(0m, "neither issuer has a sector");
        rows.Single(row => row.InstitutionalHolderId == _holder.Id).Overlap.Should().Be(1m);
    }

    [Fact]
    public async Task RebuildQuarter_MeasuresEachIndexByItsTrackersReportInsideTheWindow()
    {
        var first = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAA",
            Name: "First",
            Cusip: "000111AA1"
        );
        var second = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BBB",
            Name: "Second",
            Cusip: "000222BB2"
        );
        await Seed(seed =>
        {
            seed.AddRange(first, second);
            seed.AddRange(
                Filing(_holder, Q2),
                Holding(first, _holder, Q2, 100, 60_000),
                Holding(second, _holder, Q2, 100, 40_000),
                Holding(_apple, _holder, Q2, 100, 100_000)
            );
            // Russell 2000 (iShares, a fiscal-month report 30 days back): the two names 60/40.
            seed.Add(Tracker("S000004344", Q2.AddDays(-30), ("000111AA1", 60), ("000222BB2", 40)));
            // Russell 1000 reported 100 days back is stale; an S&P 500 report narrowed on the way
            // in is incomplete; an S&P SmallCap 600 series that N-CEN no longer calls an index fund
            // is skipped.
            seed.Add(Tracker("S000004347", Q2.AddDays(-100), ("000111AA1", 1)));
            seed.Add(Tracker("S000004310", Q2, reported: 3, ("000111AA1", 1), ("000222BB2", 1)));
            seed.Add(Tracker("S000004313", Q2, ("000111AA1", 1)));
            Series(seed, "ncen-600", Q2.AddDays(-90), "S000004313", 7, NCenFundTypes.None, 1m);
        });

        (await Rebuild(Q2)).Should().BeTrue();

        await using var read = _fixture.CreateDbContext();
        var rows = await read.Set<HolderQuarterlyBenchmarkActiveShare>()
            .Where(row => row.InstitutionalHolderId == _holder.Id)
            .ToListAsync();
        rows.Select(row => row.BenchmarkKey)
            .Should()
            .BeEquivalentTo(["russell-2000", "13f-aggregate"]);
        var index = rows.Single(row => row.BenchmarkKey == "russell-2000");
        index.BenchmarkAsOf.Should().Be(Q2.AddDays(-30));
        index.Coverage.Should().Be(1m);
        index.Overlap.Should().Be(1m);
        // The book is 30% / 20% / 50% Apple against 60% / 40%: half of 0.3 + 0.2 + 0.5.
        index.ActiveShare.Should().Be(0.5m);
        var metrics = await Metrics(Q2);
        metrics.ClosestBenchmark.Should().Be("13f-aggregate", "the combined book is this book");
        metrics.ClosestActiveShare.Should().Be(0m);
    }

    [Fact]
    public async Task RebuildQuarter_ATrackerNameWithoutAnIssuerStaysInTheIndex()
    {
        var first = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAA",
            Name: "First",
            Cusip: "000111AA1"
        );
        var other = new InstitutionalHolder { Cik = "0000000004", Name = "Other Filer" };
        // A unit investment trust files without a series id and is read by its own CIK.
        var trust = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "DIA",
            Name: "Average Trust",
            Cik: "0001041130"
        );
        var report = Tracker(null, Q2, reported: null, ("000111AA1", 75), ("999999ZZ9", 25));
        report.RegistrantCik = null;
        report.EquityIssuerId = trust.Id;
        await Seed(seed =>
        {
            seed.AddRange(first, other, trust, report);
            seed.AddRange(
                Filing(_holder, Q2),
                Holding(first, _holder, Q2, 100, 100_000),
                Filing(other, Q2),
                Holding(_apple, other, Q2, 100, 900_000)
            );
        });

        (await Rebuild(Q2)).Should().BeTrue();

        var metrics = await Metrics(Q2);
        metrics.ClosestBenchmark.Should().Be("dow-jones");
        metrics.ClosestBenchmarkName.Should().Be("Dow Jones Industrial Average");
        metrics.ClosestActiveShare.Should().Be(0.25m, "the unresolved quarter is held by no one");
        await using var read = _fixture.CreateDbContext();
        var row = await read.Set<HolderQuarterlyBenchmarkActiveShare>()
            .SingleAsync(row =>
                row.InstitutionalHolderId == _holder.Id && row.BenchmarkKey == "dow-jones"
            );
        row.Coverage.Should().Be(0.75m);
        row.Overlap.Should().Be(0.75m);
    }

    [Fact]
    public async Task RebuildQuarter_ASuccessorTradesAgainstTheOnePredecessorItContinues()
    {
        var predecessor = new InstitutionalHolder { Cik = "0000000005", Name = "Old CIK" };
        var merged = new[]
        {
            new InstitutionalHolder { Cik = "0000000006", Name = "Merged A" },
            new InstitutionalHolder { Cik = "0000000007", Name = "Merged B" },
        };
        var mergedSuccessor = new InstitutionalHolder { Cik = "0000000008", Name = "Merged" };
        await Seed(seed =>
        {
            seed.AddRange(predecessor, mergedSuccessor);
            seed.AddRange(merged);
            seed.AddRange(
                Filing(predecessor, Q1),
                Holding(_apple, predecessor, Q1, 100, 10_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 150, 18_000),
                Succession(predecessor, _holder, 10_000, 12_000)
            );
            foreach (var filer in merged)
                seed.AddRange(
                    Filing(filer, Q1),
                    Holding(_apple, filer, Q1, 50, 5_000),
                    Succession(filer, mergedSuccessor, 10_000, 12_000)
                );
            seed.AddRange(
                Filing(mergedSuccessor, Q2),
                Holding(_apple, mergedSuccessor, Q2, 100, 12_000)
            );
        });

        (await Rebuild(Q2)).Should().BeTrue();

        var continued = await Metrics(Q2);
        continued.ComparedHolderId.Should().Be(predecessor.Id);
        continued.PreviousEquityAum.Should().Be(10_000);
        continued.IncreasedCount.Should().Be(1);
        continued.BuyValue.Should().Be(6_000m, "50 shares at the 120 quarter-end price");
        var group = await Metrics(Q2, mergedSuccessor);
        group.ComparedHolderId.Should().BeNull("a merged group's prior book cannot be divided");
        group.BuyValue.Should().BeNull();
    }

    [Fact]
    public async Task RebuildQuarter_PublishesEachFilersTradesPerSector()
    {
        var sectors = await SeedSectorBook();

        await Rebuild(Q2);

        var metrics = await Metrics(Q2);
        metrics.BuyValue.Should().Be(13_000m);
        metrics.SellValue.Should().Be(9_000m);
        (await SectorFlows(Q2))
            .Should()
            .BeEquivalentTo([
                (HolderFlowSectorGroup.Sector, (Guid?)sectors.Technology, 6_000m, 0m),
                (HolderFlowSectorGroup.Sector, (Guid?)sectors.Financials, 1_000m, 0m),
                (HolderFlowSectorGroup.ExchangeTraded, (Guid?)null, 6_000m, 4_000m),
                (HolderFlowSectorGroup.Unclassified, (Guid?)null, 0m, 5_000m),
            ]);
    }

    [Fact]
    public async Task RebuildQuarter_ReplacesTheSectorRowsOnARecompute()
    {
        var sectors = await SeedSectorBook();
        await Rebuild(Q2);

        await using (var mutate = _fixture.CreateDbContext())
            await mutate
                .Set<InstitutionalHolding>()
                .Where(holding => holding.ReportDate == Q2 && holding.Shares == 20)
                .ExecuteDeleteAsync();
        await Rebuild(Q2);

        // The fund position is now an exit at the prior quarter's 500 a share.
        (await SectorFlows(Q2))
            .Should()
            .BeEquivalentTo([
                (HolderFlowSectorGroup.Sector, (Guid?)sectors.Technology, 6_000m, 0m),
                (HolderFlowSectorGroup.Sector, (Guid?)sectors.Financials, 1_000m, 0m),
                (HolderFlowSectorGroup.ExchangeTraded, (Guid?)null, 0m, 9_000m),
                (HolderFlowSectorGroup.Unclassified, (Guid?)null, 0m, 5_000m),
            ]);
    }

    [Fact]
    public async Task RebuildQuarter_AFailedPublishKeepsThePublishedSectorRows()
    {
        await SeedSectorBook();
        await Rebuild(Q2);
        var published = await SectorFlows(Q2);
        await Seed(seed => seed.Add(Holding(_apple, _holder, Q2, 10, 1_000)));

        await using (var context = _fixture.CreateDbContext())
        {
            var failing = new FailingPublishRefreshService(
                context,
                _provider.GetRequiredService<HoldingsCorpusCoverage>()
            );
            var rebuild = () => failing.RebuildQuarter(Q2, Today, CancellationToken.None);
            await rebuild.Should().ThrowAsync<InvalidOperationException>();
        }

        (await SectorFlows(Q2)).Should().BeEquivalentTo(published);
    }

    [Fact]
    public async Task HolderFlowReader_ReadsAComputedQuarterByProfileAndSector()
    {
        await SeedSectorBook();
        await Rebuild(Q2);
        await Seed(seed =>
            seed.AddRange(
                new InstitutionalHolderProfile
                {
                    InstitutionalHolderId = _holder.Id,
                    EntityType = HolderEntityType.RegisteredAdviser,
                },
                new HolderIntelligenceQuarterState
                {
                    ReportDate = Q2,
                    ComputedAt = DateTime.UtcNow,
                    CalculationVersion = HolderIntelligenceRefreshService.CalculationVersion,
                }
            )
        );

        await using var context = _fixture.CreateDbContext();
        var reader = new HolderFlowReader(
            new InstitutionalHolderRepository(context),
            new SectorRepository(context),
            new MemoryCache(new MemoryCacheOptions())
        );
        var quarter = (await reader.GetQuarters()).Single();
        var flows = await reader.ReadQuarter(
            quarter,
            HolderMixDimension.EntityType,
            HolderFlowGrouping.Sector
        );

        var advisers = flows.Buckets.Should().ContainSingle().Which;
        advisers.Token.Should().Be("adv");
        advisers.BuyValue.Should().Be(13_000m);
        advisers.SellValue.Should().Be(9_000m);
        advisers.PriorEquityAum.Should().Be(24_000);
        advisers
            .Sectors.Select(sector => (sector.Name, sector.NetValue))
            .Should()
            .Equal(
                ("Financials", 1_000m),
                ("Technology", 6_000m),
                (HolderFlowCalculator.ExchangeTradedLabel, 2_000m),
                (HolderFlowCalculator.UnclassifiedLabel, -5_000m)
            );
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
        // Postgres keeps microseconds, so the stored stamp drops the last tick digit.
        states[0]
            .DirtyAt.Should()
            .BeCloseTo(
                stamped,
                TimeSpan.FromMilliseconds(1),
                "a quarter before the corpus is never drained"
            );
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

    [Fact]
    public async Task RebuildQuarter_PricesTradesAtTheQuarterAveragePrice()
    {
        await SeedAppleBoughtAndMicrosoftSold(Bars(_apple, 8m, 10m));

        await Rebuild(Q2);

        var metrics = await Metrics(Q2);
        metrics
            .BuyValue.Should()
            .BeApproximately(100m * 210m / 26m, 0.01m, "25 sessions at 8 and one at 10");
        metrics.SellValue.Should().Be(1_000m, "Microsoft has no bars, so its exit keeps 10");
    }

    [Fact]
    public async Task RebuildQuarter_IgnoresAQuarterAverageOffTheFiledBasis()
    {
        await SeedAppleBoughtAndMicrosoftSold(Bars(_apple, 4m, 5m));

        await Rebuild(Q2);

        (await Metrics(Q2))
            .BuyValue.Should()
            .Be(1_000m, "a quarter-end close of 5 disagrees with the 10 filers valued it at");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RebuildQuarter_RestatesTheQuarterAverageAcrossAnAppliedLaterSplit(
        bool applied
    )
    {
        var split = Split(_apple, new DateOnly(2024, 8, 15), "AAPL");
        split.PriceAdjustmentAppliedTime = applied
            ? new DateTime(2024, 8, 16, 0, 0, 0, DateTimeKind.Utc)
            : null;
        await Seed(seed => seed.Add(split));
        // Before the split is applied the closes still sit on the filing basis and match it.
        await SeedAppleBoughtAndMicrosoftSold(
            applied ? Bars(_apple, 4m, 5m) : Bars(_apple, 8m, 10m)
        );

        await Rebuild(Q2);

        var buyValue = (await Metrics(Q2)).BuyValue;
        if (applied)
            buyValue
                .Should()
                .BeApproximately(100m * 210m / 26m, 0.01m, "the closes are on the 2-for-1 basis");
        else
            buyValue.Should().Be(1_000m, "an unapplied split leaves the closes' basis unproven");
    }

    [Fact]
    public async Task RebuildQuarter_DoesNotAverageAQuarterWithASplitInside()
    {
        await Seed(seed =>
        {
            seed.AddRange(Bars(_apple, 8m, 10m));
            seed.AddRange(
                Split(_apple, new DateOnly(2024, 5, 15), "AAPL"),
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 2_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 400, 4_000)
            );
        });

        await Rebuild(Q2);

        (await Metrics(Q2))
            .BuyValue.Should()
            .Be(2_000m, "200 post-split shares bought at the 10 quarter-end value per share");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RebuildQuarter_PricesEachShareClassFromItsOwnListing(bool siblingTraded)
    {
        var berkshire = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BRKA",
            Name: "Berkshire",
            SecondaryTickers: ["BRKB"]
        );
        var classB = berkshire
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "BRKB");
        await Seed(seed => seed.Add(berkshire));
        await Seed(seed =>
        {
            // The classes trade within 2% of each other, so only the listing keeps them apart.
            seed.AddRange(Bars(berkshire, 8m, 10m));
            if (siblingTraded)
                seed.AddRange(Bars(classB, 8.16m, 10.2m));
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(berkshire, _holder, Q1, 10, 100),
                Holding(berkshire, _holder, Q1, 100, 1_020, listedTicker: "BRKB"),
                Filing(_holder, Q2),
                Holding(berkshire, _holder, Q2, 20, 200),
                Holding(berkshire, _holder, Q2, 200, 2_040, listedTicker: "BRKB")
            );
        });

        await Rebuild(Q2);

        var classA = 10m * 210m / 26m;
        (await Metrics(Q2))
            .BuyValue.Should()
            .BeApproximately(
                classA + (siblingTraded ? 100m * 214.2m / 26m : 1_020m),
                0.01m,
                "class B is priced from its own bars, or at its quarter-end value without them"
            );
    }

    [Fact]
    public async Task RebuildQuarter_ChecksTheAverageAgainstTheMedianFilerMark()
    {
        var second = new InstitutionalHolder { Cik = "0000000002", Name = "Filer Two" };
        var third = new InstitutionalHolder { Cik = "0000000003", Name = "Filer Three" };
        await Seed(seed => seed.AddRange(second, third));
        await SeedAppleBoughtAndMicrosoftSold(Bars(_apple, 8m, 10m));
        var outlier = Holding(_apple, third, Q2, 10, 100);
        outlier.FiledValue = 100_000;
        await Seed(seed =>
            seed.AddRange(
                Filing(second, Q2),
                Holding(_apple, second, Q2, 50, 500),
                Filing(third, Q2),
                outlier
            )
        );

        await Rebuild(Q2);

        (await Metrics(Q2))
            .BuyValue.Should()
            .BeApproximately(
                100m * 210m / 26m,
                0.01m,
                "one filer's thousandfold units error does not move the median mark of 10"
            );
    }

    [Fact]
    public async Task RebuildQuarter_AnEarlierQuartersSplitStillAllowsTheAverage()
    {
        var split = Split(_apple, new DateOnly(2024, 2, 15), "AAPL");
        split.PriceAdjustmentAppliedTime = new DateTime(2024, 2, 16, 0, 0, 0, DateTimeKind.Utc);
        await Seed(seed => seed.Add(split));
        await SeedAppleBoughtAndMicrosoftSold(Bars(_apple, 8m, 10m));

        await Rebuild(Q2);

        (await Metrics(Q2))
            .BuyValue.Should()
            .BeApproximately(100m * 210m / 26m, 0.01m, "the February split is not inside Q2");
    }

    [Fact]
    public async Task RebuildQuarter_LeavesSharesTheReportedValueCannotCoverUnpriced()
    {
        // The Q1 filing swapped its columns: 1,000 shares worth 10,000 read as 10,000 worth 1,000.
        await Seed(seed =>
        {
            seed.AddRange(Bars(_apple, 8m, 10m));
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 10_000, 1_000),
                Holding(_microsoft, _holder, Q1, 100, 1_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 1_000, 10_000)
            );
        });

        await Rebuild(Q2);

        var metrics = await Metrics(Q2);
        metrics.UnpricedTradeCount.Should().Be(1);
        metrics.ReducedCount.Should().Be(0, "inconsistent counts give no direction");
        metrics.BuyValue.Should().Be(0m);
        metrics.SellValue.Should().Be(1_000m, "only the Microsoft exit is priced");
        (await SectorFlows(Q2))
            .Should()
            .BeEquivalentTo([(HolderFlowSectorGroup.Unclassified, (Guid?)null, 0m, 1_000m)]);
    }

    private Task SeedAppleBoughtAndMicrosoftSold(List<EquityDailyStockPrice> bars) =>
        Seed(seed =>
        {
            seed.AddRange(bars);
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(_apple, _holder, Q1, 100, 1_000),
                Holding(_microsoft, _holder, Q1, 100, 1_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 200, 2_000)
            );
        });

    [Fact]
    public async Task RebuildQuarter_ReadsTheAdvisersSeriesAsOfTheQuarterEnd()
    {
        var adviser = new InstitutionalHolder
        {
            Cik = "0000000003",
            Name = "Index Adviser",
            CrdNumber = "123",
        };
        await Seed(seed =>
        {
            seed.AddRange(adviser, Filing(adviser, Q2), Holding(_apple, adviser, Q2, 100, 10_000));
            Series(seed, "A1", new DateOnly(2024, 3, 31), "S1", 123, NCenFundTypes.IndexFund, 300m);
            // A fiscal year ending on the quarter end is that quarter's state.
            Series(seed, "A6", Q2, "S5", 123, NCenFundTypes.IndexFund, 50m);
            // The same series' next report, for a period after the quarter, is not yet its state.
            Series(seed, "A2", new DateOnly(2024, 9, 30), "S1", 123, NCenFundTypes.IndexFund, 999m);
            // A report 21 months before the quarter end still counts; an older one does not.
            Series(seed, "A3", new DateOnly(2022, 9, 30), "S2", 123, NCenFundTypes.None, 100m);
            Series(
                seed,
                "A4",
                new DateOnly(2022, 6, 30),
                "S3",
                123,
                NCenFundTypes.IndexFund,
                5_000m
            );
            Series(
                seed,
                "A5",
                new DateOnly(2024, 7, 31),
                "S4",
                123,
                NCenFundTypes.IndexFund,
                7_000m
            );
        });

        (await Rebuild(Q2)).Should().BeTrue();

        var metrics = await Metrics(Q2, adviser);
        metrics.IndexSeriesNetAssets.Should().Be(350m);
        metrics.AdvisedSeriesNetAssets.Should().Be(450m);
        metrics.AdvisedSeriesAsOf.Should().Be(Q2);
    }

    [Fact]
    public async Task RebuildQuarter_TakesTheProfilesAdviserBeforeTheCoverCrd()
    {
        var profiled = new InstitutionalHolder
        {
            Cik = "0000000003",
            Name = "Profiled Filer",
            CrdNumber = "111",
        };
        var coverOnly = new InstitutionalHolder
        {
            Cik = "0000000004",
            Name = "Cover Only Filer",
            CrdNumber = "111",
        };
        await Seed(seed =>
        {
            seed.AddRange(
                profiled,
                coverOnly,
                new InstitutionalHolderProfile
                {
                    InstitutionalHolderId = profiled.Id,
                    EntityType = HolderEntityType.InvestmentAdviser,
                    AdviserCrd = 222,
                },
                // Every holder has a profile; most link no Form ADV adviser.
                new InstitutionalHolderProfile { InstitutionalHolderId = coverOnly.Id },
                new InstitutionalHolderProfile { InstitutionalHolderId = _holder.Id },
                Filing(profiled, Q2),
                Holding(_apple, profiled, Q2, 100, 10_000),
                Filing(coverOnly, Q2),
                Holding(_apple, coverOnly, Q2, 100, 10_000),
                Filing(_holder, Q2),
                Holding(_apple, _holder, Q2, 100, 10_000)
            );
            Series(seed, "A1", Q1, "S1", 111, NCenFundTypes.IndexFund, 100m);
            Series(seed, "A2", Q1, "S2", 222, NCenFundTypes.IndexFund, 500m);
        });

        (await Rebuild(Q2)).Should().BeTrue();

        (await Metrics(Q2, profiled)).IndexSeriesNetAssets.Should().Be(500m);
        (await Metrics(Q2, coverOnly)).IndexSeriesNetAssets.Should().Be(100m);
        (await Metrics(Q2)).AdvisedSeriesAsOf.Should().BeNull("Filer One names no adviser");
    }

    private static List<EquityDailyStockPrice> Bars(
        EquityIssuer issuer,
        decimal close,
        decimal lastClose
    ) => Bars(issuer.Presentation.Listing, close, lastClose);

    // 25 weekday sessions from April 1 at one close, then the quarter's last session at another,
    // plus a later untraded bar whose close must be ignored.
    private static List<EquityDailyStockPrice> Bars(
        EquityListing listing,
        decimal close,
        decimal lastClose
    ) =>
        Enumerable
            .Range(0, 60)
            .Select(day => new DateOnly(2024, 4, 1).AddDays(day))
            .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Take(25)
            .Select(date => (Date: date, Close: close))
            .Append((Date: new DateOnly(2024, 6, 28), Close: lastClose))
            .Select(bar => (bar.Date, bar.Close, Volume: 1_000L))
            .Append((Date: new DateOnly(2024, 6, 29), Close: lastClose * 10, Volume: 0L))
            .Select(bar => new EquityDailyStockPrice
            {
                EquityListingId = listing.Id,
                SourceTicker = listing.Ticker,
                Date = bar.Date,
                Open = bar.Close,
                High = bar.Close,
                Low = bar.Close,
                Close = bar.Close,
                AdjustedClose = bar.Close,
                Volume = bar.Volume,
            })
            .ToList();

    private async Task<bool> Rebuild(DateOnly reportDate)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<HolderIntelligenceRefreshService>()
            .RebuildQuarter(reportDate, Today, CancellationToken.None);
    }

    private TestDrainWorker Drain() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), beforeRebuild: null);

    private Task<HolderQuarterlyMetrics> Metrics(DateOnly reportDate) =>
        Metrics(reportDate, _holder);

    private async Task<HolderQuarterlyMetrics> Metrics(
        DateOnly reportDate,
        InstitutionalHolder holder
    )
    {
        await using var read = _fixture.CreateDbContext();
        return await read.Set<HolderQuarterlyMetrics>()
            .SingleAsync(row =>
                row.InstitutionalHolderId == holder.Id && row.ReportDate == reportDate
            );
    }

    // A Q1 to Q2 book over a technology stock, a fund, a bank's common and its exchange-traded
    // note, and Microsoft, which has no sector: 13,000 bought and 9,000 sold at quarter-end prices.
    private async Task<(Guid Technology, Guid Financials)> SeedSectorBook()
    {
        var technology = new Sector { Name = "Technology" };
        var financials = new Sector { Name = "Financials" };
        var chips = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "Nvidia",
            Industry: new Industry { Name = "Semiconductors", SectorId = technology.Id }
        );
        var fund = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Index Fund",
            ReferenceTickers: ["SPY"]
        );
        var bank = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BANK",
            Name: "Bank",
            Industry: new Industry { Name = "Banks", SectorId = financials.Id },
            ReferenceTickers: ["BKETN"]
        );
        await Seed(seed =>
        {
            seed.AddRange(technology, financials, chips, fund, bank);
            seed.AddRange(
                Filing(_holder, Q1),
                Holding(chips, _holder, Q1, 100, 10_000),
                Holding(fund, _holder, Q1, 10, 5_000),
                Holding(bank, _holder, Q1, 40, 4_000, listedTicker: "BKETN"),
                Holding(_microsoft, _holder, Q1, 50, 5_000),
                Filing(_holder, Q2),
                Holding(chips, _holder, Q2, 150, 18_000),
                Holding(fund, _holder, Q2, 20, 12_000),
                Holding(bank, _holder, Q2, 10, 1_000)
            );
        });
        return (technology.Id, financials.Id);
    }

    private async Task<List<(HolderFlowSectorGroup, Guid?, decimal, decimal)>> SectorFlows(
        DateOnly reportDate
    )
    {
        await using var read = _fixture.CreateDbContext();
        return (
            await read.Set<HolderQuarterlySectorFlow>()
                .Where(row =>
                    row.InstitutionalHolderId == _holder.Id && row.ReportDate == reportDate
                )
                .ToListAsync()
        )
            .Select(row => (row.SectorGroup, row.SectorId, row.BuyValue, row.SellValue))
            .ToList();
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
            FiledValue = value,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"{holder.Cik}-{reportDate:yyyyMMdd}-{type}",
            Cusip = (listedTicker ?? issuer.Presentation.Listing.Ticker).PadRight(9, '0'),
        };

    // One N-CEN report naming one series and its primary adviser.
    private static void Series(
        EquiblesFinancialDbContext seed,
        string accession,
        DateOnly period,
        string seriesId,
        int crd,
        NCenFundTypes fundTypes,
        decimal netAssets
    )
    {
        var report = new NCenRegistrantReport
        {
            AccessionNumber = accession,
            RegistrantCik = "0000009000",
            RegistrantName = "Fund Trust",
            InvestmentCompanyType = "N-1A",
            FilingDate = period.AddMonths(2),
            ReportEndingPeriod = period,
        };
        var fund = new NCenFundReport
        {
            RegistrantReportId = report.Id,
            FundId = accession + "-" + seriesId,
            SeriesId = seriesId,
            FundName = "Fund " + seriesId,
            RegistrantCik = report.RegistrantCik,
            AccessionNumber = accession,
            FilingDate = report.FilingDate,
            ReportEndingPeriod = period,
            FundTypes = fundTypes,
            MonthlyAverageNetAssets = netAssets,
        };
        fund.Advisers.Add(
            new NCenFundAdviser
            {
                FundReportId = fund.Id,
                Role = NCenAdviserRole.InvestmentAdviser,
                Name = "Adviser " + crd,
                Crd = crd,
            }
        );
        seed.AddRange(report, fund);
    }

    private static HolderSuccession Succession(
        InstitutionalHolder predecessor,
        InstitutionalHolder successor,
        long predecessorGroupValue,
        long successorGroupValue
    ) =>
        new()
        {
            PredecessorHolderId = predecessor.Id,
            SuccessorHolderId = successor.Id,
            PredecessorLastReportDate = Q1,
            SuccessorFirstReportDate = Q2,
            Basis = default,
            PredecessorGroupValue = predecessorGroupValue,
            SuccessorGroupValue = successorGroupValue,
        };

    // One index-tracking fund's NPORT-P report of equity-common lines, stored whole unless the
    // reported count says otherwise.
    private static NportFiling Tracker(
        string seriesId,
        DateOnly period,
        params (string Cusip, decimal Value)[] lines
    ) => Tracker(seriesId, period, lines.Length, lines);

    private static NportFiling Tracker(
        string seriesId,
        DateOnly period,
        int? reported,
        params (string Cusip, decimal Value)[] lines
    )
    {
        var filing = new NportFiling
        {
            RegistrantCik = "1100663",
            AccessionNumber = $"nport-{seriesId}-{period:yyyyMMdd}",
            FilingDate = period.AddDays(55),
            SeriesId = seriesId,
            SeriesName = "Tracker " + seriesId,
            ReportPeriodDate = period,
            ReportPeriodEnd = period,
            ReportedHoldingCount = reported ?? lines.Length,
            ParserVersion = NportFiling.CurrentParserVersion,
        };
        filing.Holdings.AddRange(
            lines.Select(line => new NportHolding
            {
                NportFilingId = filing.Id,
                Name = "Line " + line.Cusip,
                Cusip = line.Cusip,
                Balance = 1,
                Units = "NS",
                Currency = "USD",
                ValueUsd = line.Value,
                PayoffProfile = "Long",
                AssetCategory = "EC",
                IssuerCategory = "CORP",
            })
        );
        return filing;
    }

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
            [new AggregateHolderBenchmarkProvider(dbContext)],
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

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Extensions;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for the open-window combined lane of
/// <see cref="HoldingsAggregateRefreshService"/>: while the newest quarter's 45-day
/// filing window is open, <see cref="StockQuarterlyActivityCombined"/> must carry
/// non-filers forward at their prior-quarter positions (a fund that has not filed yet
/// is assumed to still hold), count only genuinely-filed initiations/exits, and be
/// retired outright once the window closes. Window state is pinned by seeding report
/// dates relative to today — inside the 45-day deadline for the open cases, far past
/// it for the closed case (CombinedQuarterHelper owns the rule).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsAggregateRefreshServiceCombinedLaneTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsAggregateRefreshServiceCombinedLaneTests(ParadeDbFixture fixture) =>
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

    // Open-window pair: the "quarter end" sits 20 days back, inside the 45-day window.
    private static readonly DateOnly OpenCur = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);
    private static readonly DateOnly OpenPrev = OpenCur.AddDays(-92);
    private static readonly DateOnly OpenOld = OpenCur.AddDays(-184);

    // Closed-window quarter: 100 days back, past the 45-day deadline.
    private static readonly DateOnly ClosedCur = DateOnly
        .FromDateTime(DateTime.UtcNow)
        .AddDays(-100);

    private HoldingsAggregateRefreshService BuildService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
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

    [Fact]
    public async Task RebuildQuarterAsync_WindowOpen_CarriesNonFilersForwardAndCountsRealChurn()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        EquityIssuer aapl = await SeedStock(seed, "AAPL", industry);
        EquityIssuer msft = await SeedStock(seed, "MSFT", industry);
        var holderA = await SeedHolder(seed, "H001"); // filed both quarters
        var holderB = await SeedHolder(seed, "H002"); // has NOT filed the open quarter → carried forward
        var holderC = await SeedHolder(seed, "H003"); // filed last quarter without AAPL, buys it now
        var holderD = await SeedHolder(seed, "H004"); // filed the open quarter but dropped AAPL
        seed.AddRange(
            MakeHolding(aapl, holderA, OpenPrev, 100_000, "acc-a-prev"),
            MakeHolding(aapl, holderB, OpenPrev, 50_000, "acc-b-prev"),
            MakeHolding(aapl, holderD, OpenPrev, 10_000, "acc-d-prev"),
            MakeHolding(msft, holderC, OpenPrev, 20_000, "acc-c-prev"),
            MakeHolding(aapl, holderA, OpenCur, 120_000, "acc-a-cur"),
            MakeHolding(aapl, holderC, OpenCur, 30_000, "acc-c-cur"),
            // D's open-quarter filing holds only MSFT — proof D filed, so no AAPL
            // carry-forward, and D counts as a genuine AAPL exit.
            MakeHolding(msft, holderD, OpenCur, 40_000, "acc-d-cur")
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<StockQuarterlyActivityCombined>()
            .SingleAsync(s => s.EquityIssuerId == aapl.Id && s.ReportDate == OpenCur);

        row.PreviousReportDate.Should().Be(OpenPrev);
        // MakeHolding stores Shares = value / 100.
        row.CurrentShares.Should()
            .Be(2_000, "A's 1200 + C's 300 + B's 500 carried forward; D filed, so no carry");
        row.PreviousShares.Should().Be(1_600, "A 1000 + B 500 + D 100 last quarter");
        row.CurrentValue.Should().Be(200_000);
        row.PreviousValue.Should().Be(160_000);
        row.CurrentFilerCount.Should().Be(3, "A and C filed, B is carried forward");
        row.PreviousFilerCount.Should().Be(3);
        row.NewFilerCount.Should().Be(1, "only C initiated");
        row.SoldOutFilerCount.Should()
            .Be(1, "D filed this quarter without AAPL — a proven exit; B is assumed to hold");
        var listing = await read.Set<StockQuarterlyListingActivity>()
            .SingleAsync(snapshot =>
                snapshot.EquityIssuerId == aapl.Id
                && snapshot.ReportDate == OpenCur
                && snapshot.IsCombined
            );
        listing.PriceSeriesTicker.Should().Be("AAPL");
        listing.CurrentShares.Should().Be(2_000);
        listing.PreviousShares.Should().Be(1_600);
        listing.ComputedAt.Should().Be(row.ComputedAt);
    }

    // Window open: a first-time filer's book is no buy, and a CIK move compares the successor's
    // first book with the predecessor's last one instead of counting it as new beside the
    // predecessor's carried-forward book. A move whose books differ too much to be one holder
    // (a successor that took a slice) reads as a first-time filer beside a carried book.
    [Fact]
    public async Task RebuildQuarterAsync_WindowOpen_ComparesOnlyFilersWithAPriorQuarter()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        EquityIssuer stock = await SeedStock(seed, "MOVE", industry);
        var both = await SeedHolder(seed, "H101"); // filed both quarters: 1,000 -> 1,200
        var firstFiler = await SeedHolder(seed, "H102"); // first 13F: 9,000
        var predecessor = await SeedHolder(seed, "H103"); // last 13F last quarter: 5,000
        var successor = await SeedHolder(seed, "H104"); // its CIK move: 5,500
        var slicedPredecessor = await SeedHolder(seed, "H105"); // 2,000, carried forward
        var sliceSuccessor = await SeedHolder(seed, "H106"); // took a 600 slice
        seed.AddRange(
            MakeHolding(stock, both, OpenPrev, 100_000, "acc-both-prev"),
            MakeHolding(stock, both, OpenCur, 120_000, "acc-both-cur"),
            MakeHolding(stock, firstFiler, OpenCur, 900_000, "acc-first-cur"),
            MakeHolding(stock, predecessor, OpenPrev, 500_000, "acc-pred-prev"),
            MakeHolding(stock, successor, OpenCur, 550_000, "acc-succ-cur"),
            MakeHolding(stock, slicedPredecessor, OpenPrev, 200_000, "acc-sliced-prev"),
            MakeHolding(stock, sliceSuccessor, OpenCur, 60_000, "acc-slice-cur"),
            Succession(predecessor, successor, 500_000, 550_000),
            Succession(slicedPredecessor, sliceSuccessor, 200_000, 60_000)
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<StockQuarterlyActivityCombined>()
            .SingleAsync(s => s.EquityIssuerId == stock.Id && s.ReportDate == OpenCur);
        // MakeHolding stores Shares = value / 100.
        row.CurrentShares.Should()
            .Be(23_300, "every filed book plus both predecessors carried forward");
        row.PreviousShares.Should().Be(8_000);
        row.UnmatchedCurrentShares.Should()
            .Be(
                14_600,
                "the first filer's 9,000, the moved predecessor's carried 5,000 and the slice's 600"
            );
        row.UnmatchedCurrentValue.Should().Be(1_460_000);
        row.UnmatchedPreviousShares.Should()
            .Be(0, "a filer that has not filed yet is carried forward");
        row.UnmatchedPreviousValue.Should().Be(0);
        var listing = await read.Set<StockQuarterlyListingActivity>()
            .SingleAsync(snapshot =>
                snapshot.EquityIssuerId == stock.Id
                && snapshot.ReportDate == OpenCur
                && snapshot.IsCombined
            );
        listing.UnmatchedCurrentShares.Should().Be(14_600);
        listing.UnmatchedPreviousShares.Should().Be(0);

        var served = (
            await new InstitutionalHoldingRepository(read).GetMarketActivitySnapshotBacked(
                OpenCur,
                OpenPrev,
                combined: true
            )
        ).Single(a => a.CommonStockId == stock.Id);
        served
            .DeltaShares.Should()
            .Be(700, "the filer of both quarters added 200 and the moved book grew 500");
        served.DeltaValue.Should().Be(70_000);
        var live = await new InstitutionalHoldingRepository(read)
            .GetQuarterlyActivityCombined(OpenCur, OpenPrev)
            .SingleAsync(a => a.CommonStockId == stock.Id);
        live.UnmatchedCurrentShares.Should().Be(14_600);
        live.DeltaShares.Should().Be(700);

        // The as-filed snapshot of the same quarter pairs the move the same way: only the first
        // filer, the slice and the sliced predecessor's unfiled book stay out.
        var filed = await read.Set<StockQuarterlyActivity>()
            .SingleAsync(s => s.EquityIssuerId == stock.Id && s.ReportDate == OpenCur);
        (filed.UnmatchedCurrentShares, filed.UnmatchedPreviousShares).Should().Be((9_600L, 2_000L));
        filed.ToActivity().DeltaShares.Should().Be(700, "both lanes agree on the matched change");
    }

    [Fact]
    public async Task RebuildQuarterAsync_WindowOpen_NormalizesCarriedSharesToCurrentBasis()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        EquityIssuer stock = await SeedStock(seed, "SPLT", industry);
        var reporter = await SeedHolder(seed, "H010");
        var nonFiler = await SeedHolder(seed, "H011");
        seed.AddRange(
            MakeHolding(stock, reporter, OpenPrev, 100_000, "acc-reporter-prev"),
            MakeHolding(stock, nonFiler, OpenPrev, 50_000, "acc-carried-prev"),
            MakeHolding(stock, reporter, OpenCur, 120_000, "acc-reporter-cur"),
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EquityListingId = stock.Presentation.EquityListingId,
                PriceSeriesTicker = stock.Presentation.Listing.Ticker,
                EffectiveDate = OpenPrev.AddDays(30),
                Numerator = 2,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);

        await using var read = FreshContext();
        var row = await read.Set<StockQuarterlyActivityCombined>()
            .SingleAsync(snapshot =>
                snapshot.EquityIssuerId == stock.Id && snapshot.ReportDate == OpenCur
            );
        var listing = await read.Set<StockQuarterlyListingActivity>()
            .SingleAsync(snapshot =>
                snapshot.EquityIssuerId == stock.Id
                && snapshot.ReportDate == OpenCur
                && snapshot.IsCombined
            );

        row.CurrentShares.Should()
            .Be(2_200, "the filed 1,200 is already post-split and the carried 500 becomes 1,000");
        row.PreviousShares.Should().Be(1_500);
        listing.CurrentShares.Should().Be(2_200);
        listing.PreviousShares.Should().Be(1_500);
    }

    [Fact]
    public async Task RebuildQuarterAsync_WindowOpen_ExcludesInactiveStocksAtReadAndGeneration()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        EquityIssuer active = await SeedStock(seed, "LIVE", industry);
        EquityIssuer inactive = await SeedStock(seed, "GONE", industry);
        var holder = await SeedHolder(seed, "H020");
        seed.AddRange(
            MakeHolding(active, holder, OpenPrev, 100_000, "live-prev"),
            MakeHolding(active, holder, OpenCur, 120_000, "live-cur"),
            MakeHolding(inactive, holder, OpenPrev, 900_000, "gone-prev"),
            MakeHolding(inactive, holder, OpenCur, 950_000, "gone-cur")
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);

        await using (var deactivate = FreshContext())
        {
            EquityIssuer persisted = await deactivate
                .Set<EquityIssuer>()
                .SingleAsync(row => row.Id == inactive.Id);
            persisted.Presentation.Listing.Active = false;
            persisted.Presentation.Listing.DelistedOn = OpenCur;
            await deactivate.SaveChangesAsync();
        }

        await using (var staleRead = FreshContext())
        {
            (await staleRead.Set<StockQuarterlyActivityCombined>().CountAsync())
                .Should()
                .Be(2, "the materialized generation predates the directory change");
            var repository = new InstitutionalHoldingRepository(staleRead);
            var visible = await repository.GetMarketActivitySnapshots(OpenCur, true);
            visible.Should().ContainSingle(row => row.CommonStockId == active.Id);
            visible.Should().NotContain(row => row.CommonStockId == inactive.Id);
        }

        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);

        await using var rebuilt = FreshContext();
        var rows = await rebuilt.Set<StockQuarterlyActivityCombined>().ToListAsync();
        rows.Should().ContainSingle(row => row.EquityIssuerId == active.Id);
        rows.Should().NotContain(row => row.EquityIssuerId == inactive.Id);
        var listings = await rebuilt
            .Set<StockQuarterlyListingActivity>()
            .Where(row => row.IsCombined)
            .ToListAsync();
        listings.Should().ContainSingle(row => row.EquityIssuerId == active.Id);
        listings.Should().NotContain(row => row.EquityIssuerId == inactive.Id);
    }

    [Fact]
    public async Task RebuildQuarterAsync_WindowClosed_RetiresTheCombinedLane()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        EquityIssuer aapl = await SeedStock(seed, "AAPL", industry);
        var holderA = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(aapl, holderA, ClosedCur.AddDays(-92), 100_000, "acc-a-prev"),
            MakeHolding(aapl, holderA, ClosedCur, 120_000, "acc-a-cur"),
            // A stale combined row from when the window was open — must not survive
            // a rebuild after the window closed.
            new StockQuarterlyActivityCombined
            {
                EquityIssuerId = aapl.Id,
                ReportDate = ClosedCur,
                PreviousReportDate = ClosedCur.AddDays(-92),
                CurrentShares = 1,
                ComputedAt = DateTime.UtcNow,
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(ClosedCur, CancellationToken.None);

        await using var read = FreshContext();
        (await read.Set<StockQuarterlyActivityCombined>().AnyAsync())
            .Should()
            .BeFalse("the closed quarter's plain snapshot is authoritative");
    }

    [Fact]
    public async Task RebuildQuarterAsync_HistoricalQuarter_DoesNotTouchTheCombinedLane()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        EquityIssuer aapl = await SeedStock(seed, "AAPL", industry);
        var holderA = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(aapl, holderA, OpenOld, 80_000, "acc-a-old"),
            MakeHolding(aapl, holderA, OpenPrev, 100_000, "acc-a-prev"),
            MakeHolding(aapl, holderA, OpenCur, 120_000, "acc-a-cur"),
            // Marker row: a historical quarter's rebuild must neither refresh nor
            // clean the combined lane — only the open/prior quarters' rebuilds (or a
            // closed window) may touch it.
            new StockQuarterlyActivityCombined
            {
                EquityIssuerId = aapl.Id,
                ReportDate = OpenOld,
                PreviousReportDate = OpenOld.AddDays(-92),
                CurrentShares = 42,
                ComputedAt = DateTime.UtcNow,
            }
        );
        await seed.SaveChangesAsync();

        await BuildService().RebuildQuarterAsync(OpenOld, CancellationToken.None);

        await using var read = FreshContext();
        var marker = await read.Set<StockQuarterlyActivityCombined>().SingleAsync();
        marker.CurrentShares.Should().Be(42, "the historical rebuild skipped the lane");

        // The open quarter's own rebuild then refreshes the lane and sweeps the
        // stale off-quarter marker in the same pass.
        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);
        await using var read2 = FreshContext();
        var rows = await read2.Set<StockQuarterlyActivityCombined>().ToListAsync();
        rows.Should().OnlyContain(r => r.ReportDate == OpenCur);
        rows.Should().ContainSingle(r => r.EquityIssuerId == aapl.Id);
    }

    [Fact]
    public async Task RebuildQuarterAsync_PublishesHolderAndCombinedSnapshotsAtomically()
    {
        await using var seed = FreshContext();
        var industry = await SeedTaxonomy(seed);
        EquityIssuer stock = await SeedStock(seed, "AAPL", industry);
        var holderA = await SeedHolder(seed, "H001");
        var holderB = await SeedHolder(seed, "H002");
        seed.AddRange(
            MakeHolding(stock, holderA, OpenPrev, 100_000, "acc-a-prev"),
            MakeHolding(stock, holderA, OpenCur, 120_000, "acc-a-cur")
        );
        await seed.SaveChangesAsync();
        await BuildService().RebuildQuarterAsync(OpenCur, CancellationToken.None);

        await using (var import = FreshContext())
        {
            import.Add(MakeHolding(stock, holderB, OpenCur, 30_000, "acc-b-cur"));
            await import.SaveChangesAsync();
        }

        var blocking = BuildBlockingService();
        var rebuild = blocking.RebuildQuarterAsync(OpenCur, CancellationToken.None);
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var during = FreshContext();
            var visibleHolders = await during
                .Set<HolderQuarterlySnapshot>()
                .CountAsync(row => row.ReportDate == OpenCur);
            var visibleActivity = await during
                .Set<StockQuarterlyActivityCombined>()
                .SingleAsync(row => row.EquityIssuerId == stock.Id && row.ReportDate == OpenCur);

            visibleHolders.Should().Be(1, "the new holder generation is still uncommitted");
            visibleActivity
                .CurrentFilerCount.Should()
                .Be(1, "the old activity generation remains visible");
        }
        finally
        {
            blocking.Release.TrySetResult(true);
        }
        await rebuild;

        await using var after = FreshContext();
        (await after.Set<HolderQuarterlySnapshot>().CountAsync(row => row.ReportDate == OpenCur))
            .Should()
            .Be(2);
        (
            await after
                .Set<StockQuarterlyActivityCombined>()
                .SingleAsync(row => row.EquityIssuerId == stock.Id && row.ReportDate == OpenCur)
        )
            .CurrentFilerCount.Should()
            .Be(2);
    }

    private BlockingRefreshService BuildBlockingService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return new BlockingRefreshService(scopeFactory);
    }

    private static async Task<Guid> SeedTaxonomy(Equibles.Data.EquiblesFinancialDbContext ctx)
    {
        var sector = new Sector { Name = "Technology" };
        ctx.Add(sector);
        await ctx.SaveChangesAsync();
        var industry = new Industry { Name = "Software", SectorId = sector.Id };
        ctx.Add(industry);
        await ctx.SaveChangesAsync();
        return industry.Id;
    }

    private static async Task<EquityIssuer> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker,
        Guid industryId
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: $"{ticker} Corp.",
            Cik: $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}",
            IndustryId: industryId
        );
        ctx.Add(stock);
        await ctx.SaveChangesAsync();
        return stock;
    }

    // A stored CIK move from last quarter's predecessor to this quarter's successor, stamped
    // with its group's two book values as the succession refresh does.
    private static HolderSuccession Succession(
        InstitutionalHolder predecessor,
        InstitutionalHolder successor,
        long predecessorValue,
        long successorValue
    ) =>
        new()
        {
            PredecessorHolderId = predecessor.Id,
            SuccessorHolderId = successor.Id,
            PredecessorLastReportDate = OpenPrev,
            SuccessorFirstReportDate = OpenCur,
            Basis = HolderSuccessionBasis.NoticeReportedBy,
            PredecessorGroupValue = predecessorValue,
            SuccessorGroupValue = successorValue,
        };

    private static async Task<InstitutionalHolder> SeedHolder(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string cik
    )
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = $"Holder {cik}" };
        ctx.Add(holder);
        await ctx.SaveChangesAsync();
        return holder;
    }

    private static InstitutionalHolding MakeHolding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long value,
        string accession,
        FilingType filingType = FilingType.Form13F
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
            FilingType = filingType,
            Cusip =
                $"{stock.Presentation.Listing.Ticker[..Math.Min(4, stock.Presentation.Listing.Ticker.Length)]}{accession.GetHashCode():X8}"[
                    ..9
                ],
        };

    private sealed class BlockingRefreshService : HoldingsAggregateRefreshService
    {
        public BlockingRefreshService(IServiceScopeFactory scopeFactory)
            : base(scopeFactory, NullLogger<HoldingsAggregateRefreshService>.Instance) { }

        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task BeforeCombinedLaneRefresh(
            DateOnly reportDate,
            CancellationToken cancellationToken
        )
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: a stock's 13F totals count common shares only, for shares and for value. A put or
/// call row states the shares under the option and a principal row states a debt amount, so
/// neither is shares held. The row's filer still counts as a filer of the stock, and a stock
/// held only through such rows keeps its activity and listing rows. The snapshot and every live
/// query that stands in for it must give the same figures.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsCommonShareActivityTests : IAsyncLifetime
{
    private static readonly DateOnly QPrev = new(2024, 9, 30);
    private static readonly DateOnly QCur = new(2024, 12, 31);

    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];
    private EquityIssuer _held;
    private EquityIssuer _optionsOnly;
    private InstitutionalHolder _steady;
    private InstitutionalHolder _doubler;
    private InstitutionalHolder _optionFiler;
    private InstitutionalHolder _firstFiler;
    private InstitutionalHolder _lapsed;

    public HoldingsCommonShareActivityTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        await using var seed = FreshContext();
        var sector = new Sector { Name = "Technology" };
        seed.Add(sector);
        await seed.SaveChangesAsync();
        var industry = new Industry { Name = "Software", SectorId = sector.Id };
        seed.Add(industry);
        await seed.SaveChangesAsync();
        _held = await SeedStock(seed, "HELD", industry.Id);
        _optionsOnly = await SeedStock(seed, "OPTS", industry.Id);
        _steady = await SeedHolder(seed, "H001");
        _doubler = await SeedHolder(seed, "H002");
        _optionFiler = await SeedHolder(seed, "H003");
        _firstFiler = await SeedHolder(seed, "H004");
        _lapsed = await SeedHolder(seed, "H005");
        seed.AddRange(
            // Steady: 1,000 common shares both quarters, plus a put and a principal row this
            // quarter, each far larger than the shares.
            Leg(_held, _steady, QPrev, 1_000, 100_000),
            Leg(_held, _steady, QCur, 1_000, 100_000),
            Leg(_held, _steady, QCur, 900_000, 90_000_000, option: OptionType.Put),
            Leg(_held, _steady, QCur, 5_000_000, 5_000_000, shareType: ShareType.Principal),
            // Doubler: 2,000 common shares become 5,000, and a call both quarters.
            Leg(_held, _doubler, QPrev, 2_000, 200_000),
            Leg(_held, _doubler, QCur, 5_000, 500_000),
            Leg(_held, _doubler, QPrev, 300_000, 30_000_000, option: OptionType.Call),
            Leg(_held, _doubler, QCur, 300_000, 30_000_000, option: OptionType.Call),
            // Option filer: holds both stocks only through options, in both quarters.
            Leg(_held, _optionFiler, QPrev, 400_000, 40_000_000, option: OptionType.Call),
            Leg(_held, _optionFiler, QCur, 700_000, 70_000_000, option: OptionType.Call),
            Leg(_optionsOnly, _optionFiler, QPrev, 50_000, 5_000_000, option: OptionType.Put),
            Leg(_optionsOnly, _optionFiler, QCur, 80_000, 8_000_000, option: OptionType.Put),
            // First filer: no 13F for the previous quarter, so its position is no trade. Only
            // its common shares are set apart from the like-for-like change.
            Leg(_held, _firstFiler, QCur, 700, 70_000),
            Leg(_held, _firstFiler, QCur, 9_000, 900_000, option: OptionType.Put),
            // Lapsed: filed last quarter and not yet this one, so the combined view carries its
            // position forward. Only its common shares are carried into the totals.
            Leg(_held, _lapsed, QPrev, 400, 40_000),
            Leg(_held, _lapsed, QPrev, 20_000, 2_000_000, option: OptionType.Put),
            // Steady files both quarters and opens a put on the options-only stock: a new filer
            // of that stock, with no shares.
            Leg(_optionsOnly, _steady, QCur, 30_000, 3_000_000, option: OptionType.Put)
        );
        await seed.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RebuildQuarter_StockTotals_CountCommonSharesAndKeepEveryFiler()
    {
        await BuildService().RebuildQuarterAsync(QCur, CancellationToken.None);

        await using var read = FreshContext();
        var held = await read.Set<StockQuarterlyActivity>()
            .SingleAsync(
                row => row.EquityIssuerId == _held.Id && row.ReportDate == QCur,
                CancellationToken.None
            );
        held.CurrentShares.Should()
            .Be(6_700, "1,000 + 5,000 + 700 common shares; options and principal add none");
        held.PreviousShares.Should().Be(3_400);
        held.CurrentValue.Should().Be(670_000, "the value of the common shares only");
        held.PreviousValue.Should().Be(340_000);
        held.CurrentFilerCount.Should()
            .Be(4, "a filer holding only options still reported the stock");
        held.UnmatchedCurrentShares.Should().Be(700, "the first filer's put is not shares either");
        held.UnmatchedCurrentValue.Should().Be(70_000);
        held.PreviousFilerCount.Should().Be(4);
        held.UnmatchedPreviousShares.Should()
            .Be(400, "the lapsed filer's put is not shares either");
        held.UnmatchedPreviousValue.Should().Be(40_000);

        var optionsOnly = await read.Set<StockQuarterlyActivity>()
            .SingleAsync(
                row => row.EquityIssuerId == _optionsOnly.Id && row.ReportDate == QCur,
                CancellationToken.None
            );
        optionsOnly.CurrentShares.Should().Be(0);
        optionsOnly.CurrentValue.Should().Be(0);
        optionsOnly.CurrentFilerCount.Should().Be(2);
        optionsOnly.NewFilerCount.Should().Be(1, "an option position still makes a new filer");

        var listings = await read.Set<StockQuarterlyListingActivity>()
            .Where(row => row.ReportDate == QCur && !row.IsCombined)
            .ToListAsync(CancellationToken.None);
        var heldListing = listings.Single(row => row.EquityIssuerId == _held.Id);
        heldListing.UnmatchedCurrentShares.Should().Be(700);
        heldListing.UnmatchedPreviousShares.Should().Be(400);
        heldListing.CurrentShares.Should().Be(6_700);
        heldListing.PreviousShares.Should().Be(3_400);
        heldListing.CurrentValue.Should().Be(670_000);
        heldListing.CurrentFilerCount.Should().Be(4);
        heldListing
            .TopOneValue.Should()
            .Be(500_000, "the largest common-share position, not an option's");
        var optionsOnlyListing = listings.Single(row => row.EquityIssuerId == _optionsOnly.Id);
        optionsOnlyListing.CurrentShares.Should().Be(0, "the listing keeps its row with no shares");
        optionsOnlyListing.CurrentValue.Should().Be(0);
    }

    [Fact]
    public async Task LiveMarketActivity_MatchesTheRebuiltSnapshot()
    {
        await using var live = FreshContext();
        var liveRows = await new InstitutionalHoldingRepository(
            live
        ).GetMarketActivitySnapshotBacked(QCur, QPrev, combined: false, CancellationToken.None);
        liveRows
            .Should()
            .HaveCount(2, "with no snapshot yet the live queries answer for both stocks");

        await BuildService().RebuildQuarterAsync(QCur, CancellationToken.None);
        await using var read = FreshContext();
        var snapshotRows = await new InstitutionalHoldingRepository(
            read
        ).GetMarketActivitySnapshots(QCur, combined: false, CancellationToken.None);

        snapshotRows.Should().HaveCount(2);
        foreach (var snapshot in snapshotRows)
        {
            var liveRow = liveRows.Single(row => row.CommonStockId == snapshot.CommonStockId);
            Figures(liveRow).Should().Be(Figures(snapshot));
            liveRow
                .ListingShares.Select(ListingFigures)
                .Should()
                .BeEquivalentTo(snapshot.ListingShares.Select(ListingFigures));
        }
    }

    [Fact]
    public async Task LiveStockHistory_MatchesTheRebuiltSnapshot()
    {
        await using var live = FreshContext();
        var liveHistory = await new InstitutionalHoldingRepository(
            live
        ).GetStockActivitySnapshotsByStockSnapshotBacked(_held, CancellationToken.None);
        var liveCurrent = liveHistory.Single(row => row.ReportDate == QCur);
        liveCurrent.CurrentShares.Should().Be(6_700);
        liveCurrent.CurrentValue.Should().Be(670_000);
        liveCurrent.CurrentFilerCount.Should().Be(4);

        await BuildService().RebuildQuarterAsync(QPrev, CancellationToken.None);
        await BuildService().RebuildQuarterAsync(QCur, CancellationToken.None);
        await using var read = FreshContext();
        var snapshotHistory = await new InstitutionalHoldingRepository(
            read
        ).GetStockActivitySnapshotsByStockSnapshotBacked(_held, CancellationToken.None);
        var snapshotCurrent = snapshotHistory.Single(row => row.ReportDate == QCur);
        (
            await read.Set<StockQuarterlyActivity>()
                .CountAsync(row => row.EquityIssuerId == _held.Id, CancellationToken.None)
        )
            .Should()
            .Be(2, "the second read is served by the stored rows, not by the live queries again");
        snapshotCurrent
            .UnmatchedCurrentShares.Should()
            .Be(700, "only a stored row carries the unmatched part");

        (
            snapshotCurrent.CurrentShares,
            snapshotCurrent.CurrentValue,
            snapshotCurrent.CurrentFilerCount
        )
            .Should()
            .Be(
                (liveCurrent.CurrentShares, liveCurrent.CurrentValue, liveCurrent.CurrentFilerCount)
            );
    }

    [Fact]
    public async Task CombinedActivity_CountsCommonSharesAndKeepsAnOptionsOnlyStock()
    {
        await using var read = FreshContext();
        var repository = new InstitutionalHoldingRepository(read);

        var activity = await repository
            .GetQuarterlyActivityCombined(QCur, QPrev)
            .ToListAsync(CancellationToken.None);
        var listings = await repository.GetLiveListingShareActivity(
            QCur,
            QPrev,
            combined: true,
            activity.Select(row => row.CommonStockId).ToArray(),
            CancellationToken.None
        );

        var held = activity.Single(row => row.CommonStockId == _held.Id);
        held.CurrentShares.Should().Be(7_100, "the lapsed filer's 400 common shares are carried");
        held.CurrentValue.Should().Be(710_000);
        held.PreviousShares.Should().Be(3_400);
        held.CurrentFilerCount.Should().Be(5);
        held.UnmatchedCurrentShares.Should().Be(700);
        held.UnmatchedCurrentValue.Should().Be(70_000);
        listings.Single(row => row.EquityIssuerId == _held.Id).CurrentShares.Should().Be(7_100);
        listings
            .Single(row => row.EquityIssuerId == _optionsOnly.Id)
            .CurrentShares.Should()
            .Be(0, "the combined snapshot refuses a stock with no listing row");
    }

    [Fact]
    public async Task CombinedStockActivity_CarriesOnlyTheCommonSharesOfAFilerYetToFile()
    {
        await using var read = FreshContext();

        var combined = await new InstitutionalHoldingRepository(
            read
        ).GetCombinedStockActivitySnapshotBacked(_held, QCur, QPrev, CancellationToken.None);

        combined.CurrentShares.Should().Be(7_100);
        combined.CurrentValue.Should().Be(710_000, "the carried put adds no value");
        combined.PreviousValue.Should().Be(340_000);
        combined.CurrentFilerCount.Should().Be(5);
    }

    [Fact]
    public async Task HolderActivity_AnOptionOrPrincipalRow_IsNoShareChange()
    {
        await using var read = FreshContext();
        var rows = await new InstitutionalHoldingRepository(read)
            .Get13FHolderActivityByStock(_held, QCur, QPrev)
            .ToListAsync(CancellationToken.None);

        var steady = rows.Single(row => row.InstitutionalHolderId == _steady.Id);
        steady
            .CurrentShares.Should()
            .Be(1_000, "the put and the principal amount are not shares bought");
        steady.PreviousShares.Should().Be(1_000);
        steady.CurrentValue.Should().Be(100_000);
        var optionFiler = rows.Single(row => row.InstitutionalHolderId == _optionFiler.Id);
        optionFiler.CurrentShares.Should().Be(0);
        optionFiler
            .CurrentPositionCount.Should()
            .Be(1, "the filer still reported a position in the stock");
    }

    [Fact]
    public async Task DoubleDown_RanksCommonShareIncreasesOnly()
    {
        await using var read = FreshContext();
        var repository = new InstitutionalHoldingRepository(read);

        var positions = await repository
            .GetDoubleDownPositions(QCur, QPrev, 50)
            .ToListAsync(CancellationToken.None);
        var combined = await repository
            .GetDoubleDownPositionsCombined(QCur, QPrev, 50)
            .ToListAsync(CancellationToken.None);

        var doubler = positions
            .Should()
            .ContainSingle("only the filer that bought shares doubled down")
            .Subject;
        doubler.InstitutionalHolderId.Should().Be(_doubler.Id);
        doubler.CurrentShares.Should().Be(5_000);
        doubler.PreviousShares.Should().Be(2_000);
        doubler.CurrentValue.Should().Be(500_000);
        combined.Should().ContainSingle().Which.InstitutionalHolderId.Should().Be(_doubler.Id);
    }

    [Fact]
    public async Task Screen_SharesAndValue_CountCommonShares()
    {
        await using var read = FreshContext();
        var rows = await new InstitutionalHoldingRepository(read)
            .Screen(new ScreenerCriteria(), QCur, QPrev)
            .ToListAsync(CancellationToken.None);

        var held = rows.Single(row => row.CommonStockId == _held.Id);
        held.CurrentShares.Should().Be(6_700);
        held.CurrentValue.Should().Be(670_000);
        held.PreviousValue.Should().Be(340_000);
        held.CurrentFilerCount.Should().Be(4);
        rows.Single(row => row.CommonStockId == _optionsOnly.Id)
            .NewFilerCount.Should()
            .Be(1, "the screener counts the option entrant as the snapshot does");
    }

    private static (long, long, long, long, long, long, long, long, int, int) Figures(
        MarketWideStockActivity row
    ) =>
        (
            row.CurrentShares,
            row.PreviousShares,
            row.CurrentValue,
            row.PreviousValue,
            row.UnmatchedCurrentShares,
            row.UnmatchedCurrentValue,
            row.UnmatchedPreviousShares,
            row.UnmatchedPreviousValue,
            row.CurrentFilerCount,
            row.PreviousFilerCount
        );

    private static (string, long, long, long, long) ListingFigures(
        StockQuarterlyListingActivity row
    ) =>
        (
            row.PriceSeriesTicker,
            row.CurrentShares,
            row.PreviousShares,
            row.UnmatchedCurrentShares,
            row.UnmatchedPreviousShares
        );

    private Equibles.Data.EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

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

    private static InstitutionalHolding Leg(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value,
        ShareType shareType = ShareType.Shares,
        OptionType? option = null
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = shareType,
            OptionType = option,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
            FilingType = FilingType.Form13F,
            Cusip = $"{stock.Presentation.Listing.Ticker}00000"[..9],
        };
}

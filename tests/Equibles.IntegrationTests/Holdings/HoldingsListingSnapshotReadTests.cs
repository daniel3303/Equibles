using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The exact-listing request reads (report dates, trend, concentration numerators) must come
/// from the listing snapshot rows the rebuild writes and must equal what the live corpus
/// aggregates say, row for row; rows the rebuild has not written fall back to the live reads.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsListingSnapshotReadTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public HoldingsListingSnapshotReadTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private static readonly DateOnly QOld = new(2024, 6, 30);
    private static readonly DateOnly QPrev = new(2024, 9, 30);
    private static readonly DateOnly QCur = new(2024, 12, 31);
    private static readonly DateOnly QNext = new(2025, 3, 31);
    private const string Sibling = "TRST.B";
    private const string SoldOut = "TRST.C";

    [Fact]
    public async Task RebuildWritesExactListingFigures_AndSnapshotBackedReadsEqualLive()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "TRST");
        var holders = new List<InstitutionalHolder>();
        for (var i = 1; i <= 13; i++)
            holders.Add(await SeedHolder(seed, $"H{i:D3}"));
        var rows = new List<InstitutionalHolding>
        {
            // Primary listing: a legacy null label and the explicit presentation ticker.
            MakeHolding(stock, holders[0], QCur, 50_000, "p-null"),
            MakeHolding(stock, holders[1], QCur, 40_000, "p-explicit", listedTicker: "TRST"),
            MakeHolding(stock, holders[0], QPrev, 30_000, "p-null-prev"),
            // Sibling listing: holder 1 has a share row and a put leg (summed before squaring),
            // holder 3 only a prior-quarter row (excluded this quarter), holder 13 a zero row.
            MakeHolding(stock, holders[0], QCur, 300, "s-h1-a", listedTicker: Sibling),
            MakeHolding(
                stock,
                holders[0],
                QCur,
                200,
                "s-h1-b",
                listedTicker: Sibling,
                optionType: OptionType.Put
            ),
            MakeHolding(stock, holders[1], QCur, 1_000, "s-h2", listedTicker: Sibling),
            MakeHolding(stock, holders[2], QPrev, 250, "s-h3-prev", listedTicker: Sibling),
            MakeHolding(stock, holders[0], QPrev, 100, "s-h1-prev", listedTicker: Sibling),
            MakeHolding(stock, holders[12], QCur, 0, "s-h13-zero", listedTicker: Sibling),
            // A sold-out listing with rows only before this quarter, and an older quarter.
            MakeHolding(stock, holders[3], QPrev, 700, "c-h4-prev", listedTicker: SoldOut),
            MakeHolding(stock, holders[3], QOld, 600, "c-h4-old", listedTicker: SoldOut),
        };
        // Nine more sibling holders so top-5 and top-10 cut through the ranking.
        for (var i = 3; i < 12; i++)
            rows.Add(
                MakeHolding(
                    stock,
                    holders[i],
                    QCur,
                    10 * (i + 1),
                    $"s-h{i + 1}",
                    listedTicker: Sibling
                )
            );
        seed.AddRange(rows);
        await seed.SaveChangesAsync();

        var liveDates = await new InstitutionalHoldingRepository(FreshContext())
            .Get13FReportDatesByListing(stock, Sibling)
            .ToListAsync();
        var liveHistory = await new InstitutionalHoldingRepository(
            FreshContext()
        ).GetListingActivityHistory(stock, Sibling);
        var liveSoldOutHistory = await new InstitutionalHoldingRepository(
            FreshContext()
        ).GetListingActivityHistory(stock, SoldOut);
        var livePrimaryHistory = await new InstitutionalHoldingRepository(
            FreshContext()
        ).GetListingActivityHistory(stock, "trst");

        var service = BuildService();
        await service.RebuildQuarterAsync(QOld, CancellationToken.None);
        await service.RebuildQuarterAsync(QPrev, CancellationToken.None);
        await service.RebuildQuarterAsync(QCur, CancellationToken.None);

        await using var read = FreshContext();
        var sibling = await read.Set<StockQuarterlyListingActivity>()
            .SingleAsync(row =>
                row.EquityIssuerId == stock.Id
                && row.ReportDate == QCur
                && !row.IsCombined
                && row.PriceSeriesTicker == Sibling
            );
        var siblingValues = rows.Where(row => row.ListedTicker == Sibling && row.ReportDate == QCur)
            .GroupBy(row => row.InstitutionalHolderId)
            .Select(group => group.Sum(row => row.Value))
            .OrderByDescending(value => value)
            .ToList();
        sibling.CurrentFilerCount.Should().Be(siblingValues.Count);
        sibling.CurrentValue.Should().Be(siblingValues.Sum());
        sibling
            .HolderValueSquaredSum.Should()
            .BeApproximately(siblingValues.Sum(value => (double)value * value), 1e-6);
        sibling.TopOneValue.Should().Be(siblingValues.Take(1).Sum());
        sibling.TopFiveValue.Should().Be(siblingValues.Take(5).Sum());
        sibling.TopTenValue.Should().Be(siblingValues.Take(10).Sum());
        siblingValues.Count.Should().BeGreaterThan(10, "the seed must exercise the top-10 cut");

        var primary = await read.Set<StockQuarterlyListingActivity>()
            .SingleAsync(row =>
                row.EquityIssuerId == stock.Id
                && row.ReportDate == QCur
                && !row.IsCombined
                && row.PriceSeriesTicker == "TRST"
            );
        primary
            .CurrentFilerCount.Should()
            .Be(2, "the null label and the explicit ticker are one series");
        primary.CurrentValue.Should().Be(90_000);
        primary.TopOneValue.Should().Be(50_000);

        var soldOut = await read.Set<StockQuarterlyListingActivity>()
            .SingleAsync(row =>
                row.EquityIssuerId == stock.Id
                && row.ReportDate == QCur
                && !row.IsCombined
                && row.PriceSeriesTicker == SoldOut
            );
        soldOut.CurrentFilerCount.Should().Be(0, "sold out this quarter is a zero, never a null");
        soldOut.CurrentValue.Should().Be(0);
        soldOut.HolderValueSquaredSum.Should().Be(0);
        soldOut.TopTenValue.Should().Be(0);

        var repository = new InstitutionalHoldingRepository(FreshContext());
        (await repository.Get13FReportDatesByListingSnapshotBacked(stock, Sibling))
            .Should()
            .Equal(liveDates);
        (await repository.Get13FReportDatesByListingSnapshotBacked(stock, SoldOut))
            .Should()
            .Equal(QPrev, QOld);
        AssertSameHistory(await repository.GetListingActivityHistory(stock, Sibling), liveHistory);
        AssertSameHistory(
            await repository.GetListingActivityHistory(stock, SoldOut),
            liveSoldOutHistory
        );
        // The presentation ticker in any spelling reads the primary series' rows.
        AssertSameHistory(
            await repository.GetListingActivityHistory(stock, "trst"),
            livePrimaryHistory
        );
        await read.Set<StockQuarterlyListingActivity>()
            .Where(row =>
                row.EquityIssuerId == stock.Id
                && row.ReportDate == QPrev
                && !row.IsCombined
                && row.PriceSeriesTicker == "TRST"
            )
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.CurrentValue, 777L));
        (
            await new InstitutionalHoldingRepository(FreshContext()).GetListingActivityHistory(
                stock,
                "trst"
            )
        )
            .Single(row => row.ReportDate == QPrev)
            .CurrentValue.Should()
            .Be(777L);

        // Proof the reads came from the snapshot: a stored figure shows up in the trend.
        await read.Set<StockQuarterlyListingActivity>()
            .Where(row =>
                row.EquityIssuerId == stock.Id
                && row.ReportDate == QPrev
                && !row.IsCombined
                && row.PriceSeriesTicker == Sibling
            )
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.CurrentValue, 123_456L));
        var marked = await new InstitutionalHoldingRepository(
            FreshContext()
        ).GetListingActivityHistory(stock, Sibling);
        marked.Single(row => row.ReportDate == QPrev).CurrentValue.Should().Be(123_456L);
        marked.Single(row => row.ReportDate == QCur).PreviousValue.Should().Be(123_456L);
    }

    [Fact]
    public async Task RowsWithoutListingFigures_FallBackToLiveReads()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "TRST");
        var holder = await SeedHolder(seed, "H001");
        seed.AddRange(
            MakeHolding(stock, holder, QPrev, 100, "s-prev", listedTicker: Sibling),
            MakeHolding(stock, holder, QCur, 300, "s-cur", listedTicker: Sibling)
        );
        await seed.SaveChangesAsync();
        var service = BuildService();
        await service.RebuildQuarterAsync(QPrev, CancellationToken.None);
        await service.RebuildQuarterAsync(QCur, CancellationToken.None);

        // A row written by a worker that predates the columns.
        await seed.Set<StockQuarterlyListingActivity>()
            .Where(row => row.EquityIssuerId == stock.Id && row.ReportDate == QPrev)
            .ExecuteUpdateAsync(set =>
                set.SetProperty(row => row.CurrentFilerCount, (int?)null)
                    .SetProperty(row => row.CurrentValue, (long?)null)
            );
        await seed.Set<StockQuarterlyListingActivity>()
            .Where(row => row.EquityIssuerId == stock.Id && row.ReportDate == QCur)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.CurrentValue, 999L));

        var repository = new InstitutionalHoldingRepository(FreshContext());
        (await repository.Get13FReportDatesByListingSnapshotBacked(stock, Sibling))
            .Should()
            .Equal(QCur, QPrev);
        var history = await repository.GetListingActivityHistory(stock, Sibling);
        history
            .Select(row => (row.ReportDate, row.CurrentValue, row.PreviousValue))
            .Should()
            .Equal((QPrev, 100L, 0L), (QCur, 999L, 100L));
    }

    [Fact]
    public async Task NewestQuarterTheRefreshHasNotReached_IsAggregatedLiveOnTopOfSnapshots()
    {
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "TRST");
        var holderA = await SeedHolder(seed, "H001");
        var holderB = await SeedHolder(seed, "H002");
        seed.AddRange(
            MakeHolding(stock, holderA, QPrev, 100, "a-prev", listedTicker: Sibling),
            MakeHolding(stock, holderA, QCur, 300, "a-cur", listedTicker: Sibling)
        );
        await seed.SaveChangesAsync();
        var service = BuildService();
        await service.RebuildQuarterAsync(QPrev, CancellationToken.None);
        await service.RebuildQuarterAsync(QCur, CancellationToken.None);

        seed.AddRange(
            MakeHolding(stock, holderA, QNext, 500, "a-next", listedTicker: Sibling),
            MakeHolding(stock, holderB, QNext, 700, "b-next", listedTicker: Sibling)
        );
        await seed.SaveChangesAsync();

        var repository = new InstitutionalHoldingRepository(FreshContext());
        (await repository.Get13FReportDatesByListingSnapshotBacked(stock, Sibling))
            .Should()
            .Equal(QNext, QCur, QPrev);
        var history = await repository.GetListingActivityHistory(stock, Sibling);
        history.Select(row => row.ReportDate).Should().Equal(QPrev, QCur, QNext);
        var newest = history[^1];
        newest.CurrentValue.Should().Be(1_200);
        newest.CurrentFilerCount.Should().Be(2);
        newest.PreviousReportDate.Should().Be(QCur);
        newest.PreviousValue.Should().Be(300);
        newest.PreviousFilerCount.Should().Be(1);
    }

    private static void AssertSameHistory(
        List<StockQuarterlyActivity> actual,
        List<StockQuarterlyActivity> expected
    )
    {
        actual
            .Select(row =>
                (
                    row.ReportDate,
                    row.PreviousReportDate,
                    row.CurrentShares,
                    row.PreviousShares,
                    row.CurrentValue,
                    row.PreviousValue,
                    row.CurrentFilerCount,
                    row.PreviousFilerCount,
                    Listing: row.ListingShares.Single().PriceSeriesTicker,
                    ListingShares: row.ListingShares.Single().CurrentShares
                )
            )
            .Should()
            .Equal(
                expected.Select(row =>
                    (
                        row.ReportDate,
                        row.PreviousReportDate,
                        row.CurrentShares,
                        row.PreviousShares,
                        row.CurrentValue,
                        row.PreviousValue,
                        row.CurrentFilerCount,
                        row.PreviousFilerCount,
                        Listing: row.ListingShares.Single().PriceSeriesTicker,
                        ListingShares: row.ListingShares.Single().CurrentShares
                    )
                )
            );
    }

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
        string ticker
    )
    {
        var sector = new Sector { Name = "Funds" };
        ctx.Add(sector);
        await ctx.SaveChangesAsync();
        var industry = new Industry { Name = "Exchange Traded", SectorId = sector.Id };
        ctx.Add(industry);
        await ctx.SaveChangesAsync();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: $"{ticker} Trust",
            Cik: $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}",
            IndustryId: industry.Id
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

    private static InstitutionalHolding MakeHolding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long value,
        string accession,
        string listedTicker = null,
        OptionType? optionType = null
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 10,
            Value = value,
            ShareType = ShareType.Shares,
            OptionType = optionType,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
            FilingType = FilingType.Form13F,
            ListedTicker = listedTicker,
            Cusip = $"TRST{accession.GetHashCode():X8}"[..9],
        };
}

using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

[Collection(ParadeDbCollection.Name)]
public class ListingSymbolStartServiceTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private ListingSymbolStartService Service() =>
        new(
            new EquityListingRepository(DbContext),
            new EquityIssuerTickerEvidenceRepository(DbContext),
            new EquityListingSymbolStartRepository(DbContext),
            new DailyShortVolumeRepository(DbContext),
            new FinraImportPartitionRepository(DbContext),
            TimeProvider.System,
            Substitute.For<ILogger<ListingSymbolStartService>>()
        );

    private static IEnumerable<DateOnly> Weekdays(DateOnly start, DateOnly end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                yield return date;
    }

    [Fact]
    public async Task Refresh_EarlierHolderStatedTheSymbol_RecordsTheStartAndLiftsItWithTheEvidence()
    {
        var fund = EquityIssuerSeed.Create(Cik: "0000990001", Ticker: "SYMS", Name: "Fund Trust");
        var earlier = EquityIssuerSeed.Create(
            Cik: "0000990002",
            Ticker: "SYMO",
            Name: "Earlier Co"
        );
        DbContext.AddRange(fund, earlier);
        var listing = NativeListingSeed.ForStock(DbContext, fund, "SYMS");
        var earlierRun = Weekdays(new DateOnly(2022, 1, 3), new DateOnly(2022, 1, 14));
        var currentRun = Weekdays(new DateOnly(2025, 6, 26), new DateOnly(2025, 7, 3));
        foreach (var date in earlierRun.Concat(currentRun))
            DbContext.Add(
                new DailyShortVolume
                {
                    EquityListingId = listing.Id,
                    ListedTicker = "SYMS",
                    Date = date,
                    ShortVolume = 1,
                    TotalVolume = 2,
                }
            );
        var evidence = new EquityIssuerTickerEvidence
        {
            EquityIssuerId = earlier.Id,
            Ticker = "SYMS",
            FiledDate = new DateOnly(2022, 1, 10),
            SourceDocumentId = Guid.NewGuid(),
            AccessionNumber = "0000990002-22-000001",
        };
        DbContext.Add(evidence);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Service().Refresh(CancellationToken.None);

        var start = await DbContext
            .Set<EquityListingSymbolStart>()
            .AsNoTracking()
            .SingleAsync(row => row.EquityListingId == listing.Id);
        start.Ticker.Should().Be("SYMS");
        start.StartsOn.Should().Be(new DateOnly(2025, 6, 26));
        start.EvidenceSource.Should().Be(ListingSymbolStartService.EvidenceSource);

        DbContext.Remove(
            await DbContext
                .Set<EquityIssuerTickerEvidence>()
                .SingleAsync(row => row.Id == evidence.Id)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Service().Refresh(CancellationToken.None);

        (
            await DbContext
                .Set<EquityListingSymbolStart>()
                .AnyAsync(row => row.EquityListingId == listing.Id)
        )
            .Should()
            .BeFalse("the start rests only on the earlier holder's cover pages");
    }

    [Fact]
    public async Task Refresh_ClaimantsOwnLineSpellsTheSymbolDifferently_RecordsNoStart()
    {
        // Evidence keeps the symbol without separators, so an issuer stating its own SYM-S line
        // reads like a statement of SYMS; its own differently spelled line says which it meant.
        var fund = EquityIssuerSeed.Create(Cik: "0000990003", Ticker: "SYMS", Name: "Fund Trust");
        var other = EquityIssuerSeed.Create(Cik: "0000990004", Ticker: "SYM-S", Name: "Other Co");
        DbContext.AddRange(fund, other);
        var listing = NativeListingSeed.ForStock(DbContext, fund, "SYMS");
        foreach (
            var date in Weekdays(new DateOnly(2022, 1, 3), new DateOnly(2022, 1, 14))
                .Concat(Weekdays(new DateOnly(2025, 6, 26), new DateOnly(2025, 7, 3)))
        )
        {
            DbContext.Add(
                new DailyShortVolume
                {
                    EquityListingId = listing.Id,
                    ListedTicker = "SYMS",
                    Date = date,
                    ShortVolume = 1,
                    TotalVolume = 2,
                }
            );
        }
        DbContext.Add(
            new EquityIssuerTickerEvidence
            {
                EquityIssuerId = other.Id,
                Ticker = "SYMS",
                FiledDate = new DateOnly(2022, 1, 10),
                SourceDocumentId = Guid.NewGuid(),
                AccessionNumber = "0000990004-22-000001",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Service().Refresh(CancellationToken.None);

        (
            await DbContext
                .Set<EquityListingSymbolStart>()
                .AnyAsync(row => row.EquityListingId == listing.Id)
        )
            .Should()
            .BeFalse();
    }

    private async Task<EquityListing> SeedHandedOverSymbol(string cik, string earlierCik)
    {
        var fund = EquityIssuerSeed.Create(Cik: cik, Ticker: "SYMS", Name: "Fund Trust");
        var earlier = EquityIssuerSeed.Create(Cik: earlierCik, Ticker: "SYMO", Name: "Earlier Co");
        DbContext.AddRange(fund, earlier);
        var listing = NativeListingSeed.ForStock(DbContext, fund, "SYMS");
        foreach (
            var date in Weekdays(new DateOnly(2022, 1, 3), new DateOnly(2022, 1, 14))
                .Concat(Weekdays(new DateOnly(2025, 6, 26), new DateOnly(2025, 7, 3)))
        )
        {
            DbContext.Add(Row(listing, "SYMS", date));
        }
        // The line traded under another symbol before; those rows are its own history.
        DbContext.Add(Row(listing, "OLDS", new DateOnly(2021, 3, 1)));
        DbContext.Add(
            new EquityIssuerTickerEvidence
            {
                EquityIssuerId = earlier.Id,
                Ticker = "SYMS",
                FiledDate = new DateOnly(2022, 1, 10),
                SourceDocumentId = Guid.NewGuid(),
                AccessionNumber = $"{earlierCik}-22-000001",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return listing;
    }

    private static DailyShortVolume Row(EquityListing listing, string ticker, DateOnly date) =>
        new()
        {
            EquityListingId = listing.Id,
            ListedTicker = ticker,
            Date = date,
            ShortVolume = 1,
            TotalVolume = 2,
        };

    private async Task<List<(string Ticker, DateOnly Date)>> StoredRows(Guid listingId) =>
        (
            await DbContext
                .Set<DailyShortVolume>()
                .AsNoTracking()
                .Where(row => row.EquityListingId == listingId)
                .OrderBy(row => row.Date)
                .Select(row => new { row.ListedTicker, row.Date })
                .ToListAsync()
        )
            .Select(row => (row.ListedTicker, row.Date))
            .ToList();

    [Fact]
    public async Task Refresh_StartHeldSinceThePreviousRefresh_WithdrawsTheEarlierRowsAndKeepsTheStart()
    {
        var listing = await SeedHandedOverSymbol("0000990005", "0000990006");

        await Service().Refresh(CancellationToken.None);
        DbContext.ChangeTracker.Clear();
        (await StoredRows(listing.Id))
            .Should()
            .HaveCount(17, "a start first recorded waits a refresh before any row goes");

        await Service().Refresh(CancellationToken.None);
        DbContext.ChangeTracker.Clear();

        var rows = await StoredRows(listing.Id);
        rows.Should()
            .NotContain(row => row.Ticker == "SYMS" && row.Date < new DateOnly(2025, 6, 26));
        rows.Should().Contain(("OLDS", new DateOnly(2021, 3, 1)));
        rows.Count(row => row.Ticker == "SYMS").Should().Be(6);
        var start = await DbContext
            .Set<EquityListingSymbolStart>()
            .AsNoTracking()
            .SingleAsync(row => row.EquityListingId == listing.Id);
        start.WithdrawnFrom.Should().Be(new DateOnly(2022, 1, 3));
        start.EarlierHolderFrom.Should().Be(new DateOnly(2022, 1, 3));
        start.EarlierHolderThrough.Should().Be(new DateOnly(2022, 1, 14));

        await Service().Refresh(CancellationToken.None);
        DbContext.ChangeTracker.Clear();

        (
            await DbContext
                .Set<EquityListingSymbolStart>()
                .AsNoTracking()
                .SingleAsync(row => row.EquityListingId == listing.Id)
        )
            .StartsOn.Should()
            .Be(
                new DateOnly(2025, 6, 26),
                "the start outlives the rows it withdrew, or the next replay files them again"
            );
    }

    [Fact]
    public async Task Refresh_WithdrawnStartLosesItsEvidence_ReopensTheWithdrawnDates()
    {
        var listing = await SeedHandedOverSymbol("0000990007", "0000990008");
        await Service().Refresh(CancellationToken.None);
        await Service().Refresh(CancellationToken.None);
        DbContext.ChangeTracker.Clear();
        var withdrawnDate = new DateOnly(2022, 1, 5);
        var currentDate = new DateOnly(2025, 6, 30);
        foreach (
            var (date, scope) in new[]
            {
                (withdrawnDate, "listings:current"),
                (withdrawnDate, "all"),
                (currentDate, "listings:current"),
            }
        )
        {
            DbContext.Add(
                new FinraImportPartition
                {
                    Dataset = ShortVolumeImportService.Dataset,
                    PartitionDate = date,
                    ScopeKey = scope,
                }
            );
        }
        DbContext.RemoveRange(
            DbContext
                .Set<EquityIssuerTickerEvidence>()
                .Where(row => row.Ticker == "SYMS" && row.AccessionNumber.StartsWith("0000990008"))
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await Service().Refresh(CancellationToken.None);
        DbContext.ChangeTracker.Clear();

        (
            await DbContext
                .Set<EquityListingSymbolStart>()
                .AnyAsync(row => row.EquityListingId == listing.Id)
        )
            .Should()
            .BeFalse();
        var markers = (
            await DbContext
                .Set<FinraImportPartition>()
                .AsNoTracking()
                .Where(row => row.Dataset == ShortVolumeImportService.Dataset)
                .Select(row => new { row.PartitionDate, row.ScopeKey })
                .ToListAsync()
        )
            .Select(row => (row.PartitionDate, row.ScopeKey))
            .ToList();
        markers
            .Should()
            .BeEquivalentTo(
                [(withdrawnDate, "all"), (currentDate, "listings:current")],
                "the withdrawn dates' listing markers reopen; the retired scope and later dates stay"
            );
    }
}

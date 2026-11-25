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
}

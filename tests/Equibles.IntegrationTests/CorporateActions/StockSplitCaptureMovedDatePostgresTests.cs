using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CorporateActions;

// The InMemory twins cannot see the (listing, date) unique index or the revision trigger; this
// retires a source's moved date against both, from the stored APH shape.
[Collection(ParadeDbCollection.Name)]
public class StockSplitCaptureMovedDatePostgresTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly RecordDate = new(2026, 8, 17);
    private static readonly DateOnly ExDate = new(2026, 9, 3);
    private static readonly DateOnly ReportedFrom = new(2026, 7, 24);

    private StockSplitCaptureManager Manager() =>
        new(new StockSplitRepository(DbContext), new EquityIssuerRepository(DbContext));

    private static CapturedSplit Reported(DateOnly date) =>
        new()
        {
            EffectiveDate = date,
            Numerator = 2m,
            Denominator = 1m,
            Source = StockSplitSource.External,
        };

    private static StockSplit Stored(
        Guid issuerId,
        Guid listingId,
        DateOnly date,
        DateTime capturedAt,
        DateTime? applied
    ) =>
        new()
        {
            EquityIssuerId = issuerId,
            EquityListingId = listingId,
            PriceSeriesTicker = "MVDT",
            EffectiveDate = date,
            Numerator = 2m,
            Denominator = 1m,
            Source = StockSplitSource.External,
            CreationTime = capturedAt,
            PriceAdjustmentAppliedTime = applied,
        };

    [Fact]
    public async Task CaptureForListing_WithdrawnDateBesideTheReportedRow_IsDeletedAndTheSurvivorKeepsItsMarker()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "MVDT");
        var applied = new DateTime(2026, 9, 4, 0, 20, 0, DateTimeKind.Utc);
        var survivor = Stored(
            issuer.Id,
            issuer.Presentation.EquityListingId,
            ExDate,
            new DateTime(2026, 8, 7, 16, 23, 16, DateTimeKind.Utc),
            applied
        );
        DbContext.AddRange(
            issuer,
            Stored(
                issuer.Id,
                issuer.Presentation.EquityListingId,
                RecordDate,
                new DateTime(2026, 8, 6, 21, 40, 13, DateTimeKind.Utc),
                new DateTime(2026, 8, 18, 6, 23, 39, DateTimeKind.Utc)
            ),
            survivor
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (
            await Manager()
                .CaptureForListing(
                    issuer.Id,
                    issuer.Presentation.EquityListingId,
                    "MVDT",
                    [Reported(ExDate)],
                    reportedFrom: ReportedFrom
                )
        )
            .Should()
            .Be(1);

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.Set<StockSplit>().AsNoTracking().SingleAsync();
        stored.Id.Should().Be(survivor.Id);
        stored.PriceAdjustmentAppliedTime.Should().Be(applied);
    }

    [Fact]
    public async Task CaptureForListing_WithdrawnDateAlone_MovesUnderTheTriggerAndReopensReconciliation()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "MVDT");
        DbContext.AddRange(
            issuer,
            Stored(
                issuer.Id,
                issuer.Presentation.EquityListingId,
                RecordDate,
                new DateTime(2026, 8, 6, 21, 40, 13, DateTimeKind.Utc),
                new DateTime(2026, 8, 18, 6, 23, 39, DateTimeKind.Utc)
            )
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (
            await Manager()
                .CaptureForListing(
                    issuer.Id,
                    issuer.Presentation.EquityListingId,
                    "MVDT",
                    [Reported(ExDate)],
                    reportedFrom: ReportedFrom
                )
        )
            .Should()
            .Be(1);

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.Set<StockSplit>().AsNoTracking().SingleAsync();
        stored.EffectiveDate.Should().Be(ExDate);
        stored.PriceAdjustmentAppliedTime.Should().BeNull();
    }
}

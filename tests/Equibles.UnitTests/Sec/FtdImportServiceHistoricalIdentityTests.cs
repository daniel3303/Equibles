using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.HostedService.Services;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceHistoricalIdentityTests
{
    [Fact]
    public void Resolve_RecycledTicker_UsesEarliestCutoffCoveringSettlementDate()
    {
        var oldId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        var map = new Dictionary<string, List<FtdImportService.HistoricalTickerIdentity>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["USED"] =
            [
                new(oldId, "USED", new DateOnly(2020, 6, 30)),
                new(laterId, "USED", new DateOnly(2024, 6, 30)),
            ],
        };

        var resolved = FtdImportService.TryResolveHistoricalIdentity(
            "USED",
            new DateOnly(2020, 6, 30),
            map,
            [],
            out var listingId
        );

        resolved.Should().BeTrue();
        listingId.Should().Be(oldId);
    }

    [Fact]
    public void Resolve_EqualCoveringCutoffs_RefusesAmbiguousIdentity()
    {
        var cutoff = new DateOnly(2020, 6, 30);
        var map = new Dictionary<string, List<FtdImportService.HistoricalTickerIdentity>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["USED"] = [new(Guid.NewGuid(), "USED", cutoff), new(Guid.NewGuid(), "USED", cutoff)],
        };

        var resolved = FtdImportService.TryResolveHistoricalIdentity(
            "USED",
            cutoff,
            map,
            [],
            out _
        );

        resolved.Should().BeFalse();
    }

    [Fact]
    public void Resolve_AfterInclusiveCutoff_RefusesIdentity()
    {
        var map = new Dictionary<string, List<FtdImportService.HistoricalTickerIdentity>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["GONE"] = [new(Guid.NewGuid(), "GONE", new DateOnly(2020, 6, 30))],
        };

        var resolved = FtdImportService.TryResolveHistoricalIdentity(
            "GONE",
            new DateOnly(2020, 7, 1),
            map,
            [],
            out _
        );

        resolved.Should().BeFalse();
    }

    [Fact]
    public void SelectCusips_DifferentValuesOnLatestDate_DropsStock()
    {
        var listingId = Guid.NewGuid();
        var listing = new CommonStockDelistedListing { Id = listingId };
        FtdImportService.ApplyHistoricalCusipEvidence(
            listing,
            [new(listingId, "111111111", new DateOnly(2020, 6, 30))]
        );
        FtdImportService.ApplyHistoricalCusipEvidence(
            listing,
            [new(listingId, "222222222", new DateOnly(2020, 6, 30))]
        );
        FtdImportService.ApplyHistoricalCusipEvidence(
            listing,
            [new(listingId, "000000000", new DateOnly(2020, 6, 29))]
        );

        listing.HistoricalCusipBackfillCandidates.Should().Equal("111111111", "222222222");
        listing.HistoricalCusipBackfillAmbiguous.Should().BeTrue();
        listing.HistoricalCusipBackfillCandidateOn.Should().Be(new DateOnly(2020, 6, 30));
    }

    [Fact]
    public void SelectCusips_SameValueClaimsMultipleStocks_DropsEveryOwner()
    {
        var listings = new[]
        {
            new CommonStockDelistedListing { HistoricalCusipBackfillCandidates = ["111111111"] },
            new CommonStockDelistedListing { HistoricalCusipBackfillCandidates = ["111111111"] },
        };

        FtdImportService.RejectContestedHistoricalCusips(listings);

        listings
            .Should()
            .OnlyContain(listing =>
                listing.HistoricalCusipBackfillCandidates.Count == 1
                && listing.HistoricalCusipBackfillCandidates[0] == "111111111"
            );
        listings.Should().OnlyContain(listing => listing.HistoricalCusipBackfillAmbiguous);
    }

    [Fact]
    public void SelectCusips_ContestedClaimFromEarlierBatch_RemainsVisibleToLaterOwner()
    {
        var first = new CommonStockDelistedListing
        {
            HistoricalCusipBackfillCandidates = ["111111111"],
        };
        var second = new CommonStockDelistedListing
        {
            HistoricalCusipBackfillCandidates = ["111111111"],
        };
        FtdImportService.RejectContestedHistoricalCusips([first, second]);

        var later = new CommonStockDelistedListing
        {
            HistoricalCusipBackfillCandidates = ["111111111"],
        };
        FtdImportService.RejectContestedHistoricalCusips([first, second, later]);

        new[] { first, second, later }
            .Should()
            .OnlyContain(listing => listing.HistoricalCusipBackfillAmbiguous);
    }
}

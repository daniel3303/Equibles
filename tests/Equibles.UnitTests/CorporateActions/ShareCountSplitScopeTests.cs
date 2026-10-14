using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

public class ShareCountSplitScopeTests
{
    private static readonly Guid Listing = Guid.NewGuid();
    private static readonly Guid Sibling = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 6, 29);

    private static StockSplit Split(
        Guid? listingId,
        StockSplitShareCountEvidence? evidence,
        decimal numerator = 1m,
        decimal denominator = 2m,
        int dayOffset = 0
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityListingId = listingId,
            EffectiveDate = Day.AddDays(dayOffset),
            Numerator = numerator,
            Denominator = denominator,
            ShareCountEvidence = evidence,
        };

    [Theory]
    [InlineData(null)]
    [InlineData(StockSplitShareCountEvidence.Confirmed)]
    public void ForListing_TheListingsOwnUncontradictedRow_Applies(
        StockSplitShareCountEvidence? evidence
    )
    {
        var own = Split(Listing, evidence);

        ShareCountSplitScope.ForListing([own], Listing).Should().Equal(own);
    }

    [Fact]
    public void ForListing_AContradictedRow_NeverApplies()
    {
        ShareCountSplitScope
            .ForListing([Split(Listing, StockSplitShareCountEvidence.Contradicted)], Listing)
            .Should()
            .BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForListing_AConfirmedRowElsewhere_IsAdoptedOnce(bool attributed)
    {
        var listingId = attributed ? Sibling : (Guid?)null;
        var confirmed = Split(listingId, StockSplitShareCountEvidence.Confirmed);
        var echo = Split(listingId, StockSplitShareCountEvidence.Confirmed, dayOffset: 3);

        ShareCountSplitScope.ForListing([confirmed, echo], Listing).Should().Equal(confirmed);
    }

    [Fact]
    public void ForListing_TheListingsOwnRowOfTheEvent_WinsOverAConfirmedSibling()
    {
        var own = Split(Listing, null);
        var sibling = Split(Sibling, StockSplitShareCountEvidence.Confirmed, dayOffset: 2);

        ShareCountSplitScope.ForListing([own, sibling], Listing).Should().Equal(own);
    }

    [Fact]
    public void ForListing_AnUnjudgedSiblingRow_StaysOnItsListing()
    {
        ShareCountSplitScope.ForListing([Split(Sibling, null)], Listing).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(StockSplitShareCountEvidence.Confirmed, false)]
    [InlineData(StockSplitShareCountEvidence.Contradicted, false)]
    public void HasUnresolvedBasis_AnUnattributedRow_IsUnresolvedOnlyUntilJudged(
        StockSplitShareCountEvidence? evidence,
        bool unresolved
    )
    {
        ShareCountSplitScope
            .HasUnresolvedBasis([Split(null, evidence)], Listing, Day.AddDays(-1))
            .Should()
            .Be(unresolved);
    }
}

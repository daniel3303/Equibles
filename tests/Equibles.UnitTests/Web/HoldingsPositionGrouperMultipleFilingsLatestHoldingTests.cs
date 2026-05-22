using Equibles.Holdings.Data.Models;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

public class HoldingsPositionGrouperMultipleFilingsLatestHoldingTests
{
    // Group aggregates same-holder filings via AggregateByHolder, which picks
    // LatestHolding = g.OrderByDescending(h => h.FilingDate).First() to surface
    // one representative filing per holder. A swap to OrderBy (ascending) would
    // silently expose the EARLIEST filing — invalidating any UI / downstream
    // logic that reads CurrentHolding for filing-specific metadata.
    [Fact]
    public void Group_MultipleFilingsForSameHolder_CurrentHoldingIsLatestByFilingDate()
    {
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "Holder" };
        var earlier = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            InstitutionalHolderId = holderId,
            InstitutionalHolder = holder,
            Shares = 40,
            Value = 400,
            FilingDate = new DateOnly(2025, 1, 1),
            ReportDate = new DateOnly(2024, 12, 31),
        };
        var later = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            InstitutionalHolderId = holderId,
            InstitutionalHolder = holder,
            Shares = 60,
            Value = 600,
            FilingDate = new DateOnly(2025, 2, 1),
            ReportDate = new DateOnly(2024, 12, 31),
        };

        var result = HoldingsPositionGrouper.Group(
            [earlier, later],
            [],
            filersWithCurrentQuarterFilings: null
        );

        var entry = result[PositionChangeType.New].Should().ContainSingle().Subject;
        entry.CurrentHolding.Id.Should().Be(later.Id);
        entry.CurrentHolding.FilingDate.Should().Be(new DateOnly(2025, 2, 1));
    }
}

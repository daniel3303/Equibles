using Equibles.Holdings.Data.Models;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

/// <summary>
/// ClassifyChange checks previousShares == 0 before currentShares == previousShares,
/// so (0, 0) returns New instead of Unchanged. A holder reporting 0 shares in both
/// quarters (e.g. principal-only positions) hasn't newly entered — they're unchanged.
/// </summary>
public class HoldingsPositionGrouperZeroSharesBothQuartersTests
{
    [Fact(Skip = "GH-2000 — ClassifyChange checks previousShares==0 before equality")]
    public void Group_ZeroSharesBothQuarters_ClassifiesAsUnchangedNotNew()
    {
        var holderId = Guid.NewGuid();
        var holder = new InstitutionalHolder { Id = holderId, Name = "Test" };
        var current = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            InstitutionalHolderId = holderId,
            InstitutionalHolder = holder,
            Shares = 0,
            Value = 500,
            FilingDate = new DateOnly(2025, 3, 15),
            ReportDate = new DateOnly(2025, 3, 31),
        };
        var previous = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            InstitutionalHolderId = holderId,
            InstitutionalHolder = holder,
            Shares = 0,
            Value = 400,
            FilingDate = new DateOnly(2024, 12, 15),
            ReportDate = new DateOnly(2024, 12, 31),
        };

        var result = HoldingsPositionGrouper.Group([current], [previous], null);

        result[PositionChangeType.Unchanged].Should().ContainSingle();
        result[PositionChangeType.New].Should().BeEmpty();
    }
}

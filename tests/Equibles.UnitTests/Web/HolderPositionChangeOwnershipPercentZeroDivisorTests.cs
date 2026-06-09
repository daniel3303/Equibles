using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

public class HolderPositionChangeOwnershipPercentZeroDivisorTests
{
    // Contract: OwnershipPercent returns the holder's current shares as a percentage
    // of shares outstanding. When shares outstanding is not positive there is no
    // meaningful percentage, so it must return null — never divide by zero into
    // Infinity/NaN, which would render as a garbage ownership figure in the table.
    [Fact]
    public void OwnershipPercent_ZeroSharesOutstanding_ReturnsNull()
    {
        var change = new HolderPositionChange { CurrentShares = 1_000_000 };

        var result = change.OwnershipPercent(0);

        result.Should().BeNull();
    }
}

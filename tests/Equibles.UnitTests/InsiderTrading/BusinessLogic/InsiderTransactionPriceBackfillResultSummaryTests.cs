using Equibles.InsiderTrading.BusinessLogic.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderTransactionPriceBackfillResultSummaryTests
{
    [Fact]
    public void Summary_PopulatedCounts_WiresEachCountToItsLabel()
    {
        // The Summary getter (zero tests) reports each documented count against its own label.
        // Distinct values catch a label↔value miswiring (e.g. Valid/Invalid swapped) that equal
        // counts would hide. Oracle from the field docs: Processed/Total, Repaired, Valid,
        // Invalid (no share count), Still pending (no close yet).
        var result = new InsiderTransactionPriceBackfillResult
        {
            Total = 10,
            Processed = 7,
            Repaired = 3,
            Valid = 2,
            Invalid = 1,
            Pending = 4,
        };

        result
            .Summary.Should()
            .Be(
                "Evaluated 7/10 transactions. Repaired: 3. Valid: 2. "
                    + "Invalid (no share count): 1. Still pending (no close yet): 4."
            );
    }
}

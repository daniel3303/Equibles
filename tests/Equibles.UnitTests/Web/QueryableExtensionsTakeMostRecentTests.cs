using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

// Lane A (adversarial): TakeMostRecent promises the "most recent N rows" — it must
// order by the key DESCENDING and cap at count, returning the highest keys newest-first.
// A shuffled source proves it actually reorders: dropping the OrderByDescending (or
// flipping it to ascending) would return the wrong rows. Oracle derived from the contract.
public class QueryableExtensionsTakeMostRecentTests
{
    [Fact]
    public void TakeMostRecent_ShuffledSource_ReturnsHighestKeysInDescendingOrder()
    {
        var source = new[] { 3, 1, 5, 2, 4 }.AsQueryable();

        var result = source.TakeMostRecent(x => x, 2).ToList();

        result.Should().Equal(5, 4);
    }
}

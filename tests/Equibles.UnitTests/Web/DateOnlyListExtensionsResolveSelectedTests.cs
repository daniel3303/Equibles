using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class DateOnlyListExtensionsResolveSelectedTests
{
    // Contract: ResolveSelectedDateOrFirst returns the requested date ONLY when it appears
    // in the list, otherwise the first (most-recent) entry. Realistic risk: a stale "?date="
    // query param naming a quarter the stock no longer has on file — the resolver must fall
    // back to the freshest available date, never echo the unavailable one (which would then
    // drive a lookup that returns nothing). Plausible regression: collapsing the logic to
    // `requested ?? available[0]` drops the Contains() guard and returns the bogus date.
    // Oracle derived from the doc-comment, before reading the body.
    [Fact]
    public void ResolveSelectedDateOrFirst_RequestedNotInList_FallsBackToFirstEntry()
    {
        var available = new List<DateOnly> { new(2024, 9, 30), new(2024, 6, 30), new(2024, 3, 31) };
        var requested = new DateOnly(2020, 1, 1);

        var resolved = available.ResolveSelectedDateOrFirst(requested);

        resolved.Should().Be(new DateOnly(2024, 9, 30));
        resolved.Should().NotBe(requested);
    }
}

using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class DateOnlyListExtensionsResolveSelectedDateOrFirstTests
{
    // Contract: return the requested date when it appears in the list, otherwise
    // the first (latest) entry. The load-bearing part is the Contains guard — an
    // absent requested value must fall back to the first, NOT pass through; and a
    // present-but-not-first value must be honored, NOT collapse to the first.
    [Fact]
    public void ResolveSelectedDateOrFirst_AbsentRequestedFallsBackToFirst_PresentRequestedHonored()
    {
        var available = new List<DateOnly> { new(2024, 9, 30), new(2024, 6, 30), new(2024, 3, 31) };

        // Present but not first → returned as-is (proves it isn't hard-wired to [0]).
        available
            .ResolveSelectedDateOrFirst(new DateOnly(2024, 3, 31))
            .Should()
            .Be(new DateOnly(2024, 3, 31));
        // Absent → falls back to the first/latest entry (proves the Contains guard).
        available
            .ResolveSelectedDateOrFirst(new DateOnly(2020, 1, 1))
            .Should()
            .Be(new DateOnly(2024, 9, 30));
    }
}

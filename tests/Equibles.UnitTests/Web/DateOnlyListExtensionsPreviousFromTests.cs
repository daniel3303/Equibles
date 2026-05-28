using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class DateOnlyListExtensionsPreviousFromTests
{
    [Fact]
    public void PreviousFrom_MiddleQuarterInDescendingList_ReturnsImmediatelyOlderQuarter()
    {
        // Contract: the list is latest-first (descending), so the quarter "immediately
        // older than current" is the entry AFTER it, not the newer one before it.
        // Pins the direction — a sign/index flip would return the newer quarter instead.
        var available = new List<DateOnly>
        {
            new(2025, 12, 31),
            new(2025, 9, 30),
            new(2025, 6, 30),
            new(2025, 3, 31),
        };

        var previous = available.PreviousFrom(new DateOnly(2025, 9, 30));

        previous.Should().Be(new DateOnly(2025, 6, 30));
    }
}

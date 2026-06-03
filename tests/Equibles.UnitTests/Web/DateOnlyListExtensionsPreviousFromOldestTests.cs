using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class DateOnlyListExtensionsPreviousFromOldestTests
{
    [Fact]
    public void PreviousFrom_OldestEntry_ReturnsNull()
    {
        // Contract (doc): in a latest-first list, PreviousFrom returns null when `current` is the
        // oldest entry — there is no older quarter. The existing test only covers the middle case,
        // leaving the `index < Count - 1` boundary unpinned; an off-by-one (`<=`) would read past
        // the end or return a bogus quarter instead of null.
        var available = new List<DateOnly>
        {
            new(2024, 12, 31),
            new(2024, 9, 30),
            new(2024, 6, 30), // oldest (last in descending list)
        };

        available.PreviousFrom(new DateOnly(2024, 6, 30)).Should().BeNull();
    }
}

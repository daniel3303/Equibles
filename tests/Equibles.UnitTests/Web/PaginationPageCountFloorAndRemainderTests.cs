using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class PaginationPageCountFloorAndRemainderTests
{
    // Sibling to the exact-multiple pin (20/10 -> 2). Two untested arms:
    // the floor-of-1 (an empty dataset must still report 1 page, never 0) and
    // the ceiling round-up on a partial last page (21 items over 10 -> 3, not 2).
    [Fact]
    public void PageCount_EmptyDatasetFloorsToOne_PartialLastPageRoundsUp()
    {
        // Floor: 0 items must not collapse to 0 pages (the page picker needs >=1).
        Pagination.PageCount(totalCount: 0, pageSize: 10).Should().Be(1);
        // Ceiling: a remainder rounds up to an extra page.
        Pagination.PageCount(totalCount: 21, pageSize: 10).Should().Be(3);
    }
}

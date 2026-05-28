using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class PaginationPageCountTests
{
    [Fact]
    public void PageCount_TotalIsExactMultipleOfPageSize_DoesNotAddPhantomPage()
    {
        // Ceiling division: 20 items at 10/page is exactly 2 pages. The classic
        // off-by-one in this pattern emits a phantom 3rd page on exact multiples;
        // pin that it doesn't.
        Pagination.PageCount(totalCount: 20, pageSize: 10).Should().Be(2);
    }
}

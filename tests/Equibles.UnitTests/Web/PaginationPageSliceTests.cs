using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class PaginationPageSliceTests
{
    // Page is 1-based: Page(2, 10) must skip the first page and return the
    // second slice (items 10..19), not 0..9 (page treated as 0-based) nor
    // 20..29 (double-skip). Pins the Skip((page-1)*pageSize) arithmetic.
    [Fact]
    public void Page_SecondPage_ReturnsExactlyThatPageSlice()
    {
        var source = Enumerable.Range(0, 30).AsQueryable();

        var pageTwo = source.Page(page: 2, pageSize: 10).ToList();

        pageTwo.Should().HaveCount(10);
        pageTwo.Should().Equal(Enumerable.Range(10, 10));
    }
}

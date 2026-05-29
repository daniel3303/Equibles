using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class PaginationClampPageTests
{
    // Contract: a non-positive page is clamped to 1. A page < 1 would make
    // Skip((page-1)*pageSize) negative, which PostgreSQL rejects (22023) and
    // surfaces as an HTTP 500 — so 0 and negatives must floor to 1, while a
    // valid page passes through unchanged.
    [Fact]
    public void ClampPage_NonPositivePagesFloorToOne_ValidPagePassesThrough()
    {
        Pagination.ClampPage(0).Should().Be(1);
        Pagination.ClampPage(-5).Should().Be(1);
        Pagination.ClampPage(3).Should().Be(3);
    }
}

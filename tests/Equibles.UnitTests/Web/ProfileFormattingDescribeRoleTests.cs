using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class ProfileFormattingDescribeRoleTests
{
    // Pins the documented role-precedence ladder of ProfileFormatting.DescribeRole:
    // a non-blank officer title wins outright (even over director / 10% owner),
    // then director, then 10% owner, and finally null when nothing is known.
    [Fact]
    public void DescribeRole_ResolvesByPrecedence_TitleThenDirectorThenOwnerThenNull()
    {
        ProfileFormatting
            .DescribeRole("Chief Executive Officer", isDirector: true, isTenPercentOwner: true)
            .Should()
            .Be("Chief Executive Officer");

        ProfileFormatting
            .DescribeRole("   ", isDirector: true, isTenPercentOwner: true)
            .Should()
            .Be("Director");

        ProfileFormatting
            .DescribeRole(null, isDirector: false, isTenPercentOwner: true)
            .Should()
            .Be("10% owner");

        ProfileFormatting
            .DescribeRole("", isDirector: false, isTenPercentOwner: false)
            .Should()
            .BeNull();
    }
}

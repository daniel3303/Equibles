using System.Reflection;
using Equibles.InsiderTrading.Repositories.Search;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins the role-precedence contract of InsiderOwnerSearchProvider.DescribeRole
/// (new global search #885, 0% covered). An owner is routinely both an officer
/// and a director; the descriptor must show the specific officer title, not the
/// generic "Director". A regression reordering the checks would mislabel every
/// officer-director. DescribeRole is private static → reflection (repo pattern).
/// </summary>
public class InsiderOwnerSearchProviderDescribeRoleTests
{
    [Fact]
    public void DescribeRole_OfficerTitlePresentAndAlsoDirectorAndTenPercent_OfficerTitleWins()
    {
        var describeRole = typeof(InsiderOwnerSearchProvider).GetMethod(
            "DescribeRole",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (string)describeRole.Invoke(null, ["Chief Financial Officer", true, true])!;

        result.Should().Be("Chief Financial Officer");
    }
}

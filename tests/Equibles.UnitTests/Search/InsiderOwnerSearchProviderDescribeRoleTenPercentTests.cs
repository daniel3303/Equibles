using System.Reflection;
using Equibles.InsiderTrading.Repositories.Search;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins the 10%-owner arm of InsiderOwnerSearchProvider.DescribeRole, sibling
/// to the officer-title and whitespace-Director pins. A 10% owner who is not
/// also an officer and not a director must render as "10% owner". A regression
/// reordering the checks (e.g. inverting Director and TenPercent) would
/// mislabel pure 10% beneficial owners as "Director" or null, breaking the
/// "Insiders" group's role column. DescribeRole is private static — reflection
/// mirrors the existing tests' pattern.
/// </summary>
public class InsiderOwnerSearchProviderDescribeRoleTenPercentTests
{
    [Fact]
    public void DescribeRole_NoOfficerTitleNoDirectorButTenPercent_ReturnsTenPercentOwner()
    {
        var describeRole = typeof(InsiderOwnerSearchProvider).GetMethod(
            "DescribeRole",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (string)describeRole.Invoke(null, [null, false, true])!;

        result.Should().Be("10% owner");
    }
}

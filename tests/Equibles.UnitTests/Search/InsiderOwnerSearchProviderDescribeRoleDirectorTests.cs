using System.Reflection;
using Equibles.InsiderTrading.Repositories.Search;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins the whitespace-officer-title fallback of InsiderOwnerSearchProvider.DescribeRole.
/// Form 4 ownership XML occasionally ships officerTitle as a blank-but-present value (a
/// stray space, NBSP, etc.); the descriptor must not surface that whitespace as the
/// role. A regression swapping `IsNullOrWhiteSpace` for `IsNullOrEmpty` would compile
/// cleanly and silently mislabel every director whose officerTitle came through as
/// whitespace, breaking the global search's "Insiders" group. DescribeRole is private
/// static — reflection mirrors the repo pattern.
/// </summary>
public class InsiderOwnerSearchProviderDescribeRoleDirectorTests
{
    [Fact]
    public void DescribeRole_WhitespaceOfficerTitleAndDirector_ReturnsDirectorNotWhitespace()
    {
        var describeRole = typeof(InsiderOwnerSearchProvider).GetMethod(
            "DescribeRole",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (string)describeRole.Invoke(null, [" ", true, false])!;

        result.Should().Be("Director");
    }
}

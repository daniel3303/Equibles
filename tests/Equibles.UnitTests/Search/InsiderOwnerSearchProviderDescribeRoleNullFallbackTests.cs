using System.Reflection;
using Equibles.InsiderTrading.Repositories.Search;

namespace Equibles.UnitTests.Search;

/// <summary>
/// Pins the all-null fallback arm of InsiderOwnerSearchProvider.DescribeRole.
/// Completes the 4-arm coverage alongside the officer-title-wins pin,
/// the whitespace-Director pin (#1298), and the 10%-owner pin (#1299).
/// When none of the three role indicators is set, the descriptor must be
/// null — surfaced to the global search as "no subtitle", not as a literal
/// "Unknown" or empty string. A regression that returned "Unknown" or "" would
/// pollute every insider's "Insiders" group entry with a meaningless label.
/// </summary>
public class InsiderOwnerSearchProviderDescribeRoleNullFallbackTests
{
    [Fact]
    public void DescribeRole_NoOfficerTitleNoDirectorNoTenPercent_ReturnsNull()
    {
        var describeRole = typeof(InsiderOwnerSearchProvider).GetMethod(
            "DescribeRole",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = describeRole.Invoke(null, [null, false, false]);

        result.Should().BeNull();
    }
}

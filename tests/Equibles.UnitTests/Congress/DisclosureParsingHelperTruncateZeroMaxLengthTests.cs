using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Contract: Truncate(value, maxLength) returns at most maxLength characters.
/// With maxLength=0 and a non-empty value, the result should be an empty string.
/// The surrogate-pair guard indexes value[maxLength - 1], which becomes value[-1]
/// when maxLength is 0 — throwing IndexOutOfRangeException instead of returning "".
/// </summary>
public class DisclosureParsingHelperTruncateZeroMaxLengthTests
{
    [Fact]
    public void Truncate_ZeroMaxLength_ReturnsEmptyString()
    {
        var act = () => DisclosureParsingHelper.Truncate("abc", 0);

        act.Should().NotThrow("maxLength=0 must not index into value with a negative offset");
    }
}

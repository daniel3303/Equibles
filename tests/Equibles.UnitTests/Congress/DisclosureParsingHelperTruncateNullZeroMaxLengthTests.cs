using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperTruncateNullZeroMaxLengthTests
{
    // Sibling to the zero-maxLength + negative-maxLength + surrogate-pair pins.
    // The early-bail body reads `value == null ? value : string.Empty` — null
    // input survives as null when maxLength <= 0, non-null collapses to "".
    // Pin the null-passthrough on its own: downstream `Truncate(owner, 64)`
    // (DisclosureParsingHelper.cs:193) and `Truncate(assetName, 256)`
    // (line 189) feed the result straight into a DisclosureTransaction whose
    // OwnerType / AssetName columns distinguish "field absent" (null) from
    // "field intentionally empty" (""). A refactor that simplified the
    // conditional to `return string.Empty;` would silently coalesce null to
    // empty for the maxLength<=0 edge — and any future caller that relied on
    // the null-vs-empty distinction would silently break.
    [Fact]
    public void Truncate_NullValueWithZeroMaxLength_ReturnsNullNotEmpty()
    {
        var result = DisclosureParsingHelper.Truncate(null, 0);

        result.Should().BeNull();
    }
}

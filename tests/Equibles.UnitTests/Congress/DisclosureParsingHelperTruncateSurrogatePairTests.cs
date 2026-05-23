using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// DisclosureParsingHelper.Truncate caps strings to a column-width limit
/// (e.g. 256 for AssetName). The implementation uses value[..maxLength],
/// which counts UTF-16 code units. A supplementary-plane character (emoji,
/// CJK Extension B) occupies two code units (a surrogate pair). When
/// maxLength lands between the high and low surrogate, the slice orphans
/// the high surrogate — producing a string that is technically valid in
/// .NET but corrupts on round-trip through PostgreSQL (UTF-8 rejects lone
/// surrogates) or JSON serialization.
/// </summary>
public class DisclosureParsingHelperTruncateSurrogatePairTests
{
    // Truncate should not produce a string containing an unpaired surrogate.
    [Fact]
    public void Truncate_MaxLengthSplitsSurrogatePair_ResultContainsNoOrphanSurrogate()
    {
        // "🏛" (U+1F3DB, Classical Building) is a surrogate pair: 🏛
        // Place it so maxLength falls right between the two code units.
        var prefix = new string('A', 9);
        var input = prefix + "🏛" + "trailing";
        // input.Length = 9 + 2 + 8 = 19 code units
        // maxLength = 10 lands after the high surrogate (\uD83C) but before the low (\uDFDB)

        var result = DisclosureParsingHelper.Truncate(input, 10);

        var hasOrphanSurrogate = result.Any(c =>
            char.IsHighSurrogate(c)
                && (
                    result.IndexOf(c) == result.Length - 1
                    || !char.IsLowSurrogate(result[result.IndexOf(c) + 1])
                )
            || char.IsLowSurrogate(c)
                && (result.IndexOf(c) == 0 || !char.IsHighSurrogate(result[result.IndexOf(c) - 1]))
        );

        hasOrphanSurrogate
            .Should()
            .BeFalse(
                "truncation at a surrogate-pair boundary must not produce an orphan surrogate"
            );
    }
}

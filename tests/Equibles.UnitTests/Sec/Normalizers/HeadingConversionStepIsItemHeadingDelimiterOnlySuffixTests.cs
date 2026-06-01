using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsItemHeading classifies SEC 10-K "Item N" section headings: it should
/// return true only when an item identifier (a number such as "1" / "1A")
/// actually follows the word "Item". A fragment like "Item -" is a visual
/// section divider with no item number, so the contract answer is false.
/// Its sibling IsPartHeading is pinned to reject the exact parallel case
/// ("Part -" -> false), proving the intended classification; this is the
/// missing symmetric guarantee. Oracle derived from the SEC 10-K
/// item-heading convention and the sibling's contract before reading the body.
/// </summary>
public class HeadingConversionStepIsItemHeadingDelimiterOnlySuffixTests
{
    [Fact]
    public void IsItemHeading_ItemFollowedByDelimiterOnlySuffix_ReturnsFalse()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItemHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        // "Item -" has no item identifier after the whitespace — it is a divider,
        // not a numbered item heading. IsPartHeading rejects the parallel "Part -".
        var result = (bool)method.Invoke(step, ["Item -"])!;

        result
            .Should()
            .BeFalse(
                "'Item -' carries no item identifier, so it is not a SEC 10-K item heading — mirroring IsPartHeading's rejection of 'Part -'"
            );
    }
}

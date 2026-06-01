using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsItemHeading must classify a delimiter-only fragment as a divider, not an
/// item heading — it carries no item identifier. "Item -" (ASCII hyphen) is
/// already pinned; this pins the em-dash (U+2014) variant SEC EDGAR routinely
/// uses for section dividers. The em-dash is NOT in the guard's separator set,
/// so it survives tokenisation and must be rejected by the alphanumeric-token
/// check — a distinct code path from the ASCII case. Contract derived from the
/// SEC 10-K item-heading convention before reading the body.
/// </summary>
public class HeadingConversionStepIsItemHeadingEmDashDividerTests
{
    [Fact]
    public void IsItemHeading_ItemFollowedByEmDashOnly_ReturnsFalse()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItemHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        // "Item —" — an em-dash divider with no item identifier after it.
        var result = (bool)method.Invoke(step, ["Item —"])!;

        result
            .Should()
            .BeFalse(
                "an em-dash divider carries no item identifier, so it is not a SEC 10-K item heading — the alphanumeric-token guard must reject it even though the em-dash is not a recognised separator"
            );
    }
}

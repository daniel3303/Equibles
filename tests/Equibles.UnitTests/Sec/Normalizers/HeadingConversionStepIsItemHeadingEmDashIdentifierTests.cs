using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// Positive counterpart to the em-dash divider test: a real SEC item heading whose
/// identifier is glued to its title by a no-space em-dash (U+2014) — "Item 1A—Risk
/// Factors", a form EDGAR routinely emits — must be recognised as an item heading.
/// The shared keyword tokenizer treats the em-dash as a separator (GH-3866), so the
/// first token is the bare identifier "1A" and the number-led item check accepts it.
/// This pins that the dash fix covers IsItemHeading, not only the Part path, so a
/// future narrowing of the shared helper can't silently regress item detection.
/// Contract derived from the SEC item-heading convention before reading the body.
/// </summary>
public class HeadingConversionStepIsItemHeadingEmDashIdentifierTests
{
    [Fact]
    public void IsItemHeading_IdentifierGluedToTitleByEmDash_ReturnsTrue()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItemHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, ["Item 1A—Risk Factors"])!;

        result
            .Should()
            .BeTrue(
                "a number-led item identifier separated from its title by a no-space em-dash is a real SEC item heading and must be detected like the ASCII-hyphen form"
            );
    }
}

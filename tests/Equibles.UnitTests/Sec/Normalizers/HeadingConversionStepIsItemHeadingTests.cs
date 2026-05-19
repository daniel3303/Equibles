using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsItemHeading classifies SEC 10-K "Item N" section headings. SEC EDGAR
/// HTML routinely renders these with a non-breaking space (U+00A0) between
/// "Item" and the number — e.g. <c>Item&amp;nbsp;1A. Risk Factors</c>. The
/// canonical heading is the same regardless of which Unicode whitespace the
/// renderer chose, so IsItemHeading must accept the NBSP variant. Contract
/// derived from the SEC 10-K item-heading convention before reading the body.
/// </summary>
public class HeadingConversionStepIsItemHeadingTests
{
    [Fact(
        Skip = "GH-975 — IsItemHeading rejects NBSP-separated 'Item N' headings (StartsWith requires U+0020)"
    )]
    public void IsItemHeading_NonBreakingSpaceSeparator_ReturnsTrue()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItemHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        // Explicit U+00A0 between "Item" and "1A" — what SEC EDGAR emits.
        var result = (bool)method.Invoke(step, ["Item\u00A01A. Risk Factors"])!;

        result
            .Should()
            .BeTrue(
                "SEC EDGAR renders 'Item 1A' with a non-breaking space; the heading is the same canonical SEC 10-K item heading regardless of which whitespace separates 'Item' from the number"
            );
    }
}

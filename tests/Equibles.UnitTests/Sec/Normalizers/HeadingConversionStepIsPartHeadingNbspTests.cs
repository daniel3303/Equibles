using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsPartHeading classifies SEC 10-K "PART N" section headings. SEC EDGAR
/// HTML routinely renders these with a non-breaking space (U+00A0) between
/// "Part" and the roman numeral — e.g. <c>Part&amp;nbsp;IV</c>. The canonical
/// heading is the same regardless of which Unicode whitespace the renderer
/// chose, so IsPartHeading must accept the NBSP variant (the sister method
/// IsItemHeading was fixed for the identical pattern under GH-975). Contract
/// derived from the SEC 10-K part-heading convention before reading the body.
/// </summary>
public class HeadingConversionStepIsPartHeadingNbspTests
{
    [Fact(
        Skip = "GH-981 — IsPartHeading rejects NBSP-separated 'Part N' headings (StartsWith requires U+0020)"
    )]
    public void IsPartHeading_NonBreakingSpaceSeparator_ReturnsTrue()
    {
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsPartHeading",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        // Explicit U+00A0 between "Part" and "IV" — what SEC EDGAR emits.
        var result = (bool)method.Invoke(step, ["Part\u00A0IV"])!;

        result
            .Should()
            .BeTrue(
                "SEC EDGAR renders 'Part IV' with a non-breaking space; the heading is the same canonical SEC 10-K part heading regardless of which whitespace separates 'Part' from the numeral"
            );
    }
}

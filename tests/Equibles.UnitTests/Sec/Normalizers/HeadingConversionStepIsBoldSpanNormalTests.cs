using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsBoldSpanNormalTests
{
    // Sibling to HeadingConversionStepIsItalicSpanNormalTests (the parallel
    // negative-arm pin for font-style:italic). The existing IsBoldSpan
    // siblings (IsBoldSpanTests, IsBoldSpanSpacedColonTests) only pin
    // positive cases — a span carrying `font-weight:bold` returns true.
    // The negative arm — a span with an explicit non-bold font-weight —
    // is unpinned, mirroring the original italic gap.
    //
    // Same risk class as the italic sibling:
    //   • Always-true regression — every positive sibling passes
    //     whether or not the method short-circuits to true.
    //   • Inversion regression (`!Contains` → `Contains`) — positive
    //     inputs still pass under inversion (the literal is still
    //     present), but the negative case flips behavior.
    //   • Substring-confusion drift — a refactor to a bare
    //     `Contains("bold")` would still falsely match unrelated CSS
    //     property names containing "bold" (rare in practice but a
    //     defensible structural concern).
    //
    // `font-weight:normal` is production-real on SEC EDGAR's CSS reset
    // blocks (`body { font-weight: normal; }` style imports). Asserting
    // `IsBoldSpan == false` on a `font-weight:normal` span catches
    // both constant-return and inversion regressions.
    [Fact]
    public void IsBoldSpan_FontWeightNormal_ReturnsFalse()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span style=\"font-weight:normal\">Body text</span></body></html>"
            )
            .QuerySelector("span");
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsBoldSpan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span]);

        result.Should().BeFalse();
    }
}

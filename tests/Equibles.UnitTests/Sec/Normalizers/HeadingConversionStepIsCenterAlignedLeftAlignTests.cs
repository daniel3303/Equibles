using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsCenterAlignedLeftAlignTests
{
    // Completes the negative-arm trio for the three style-based heading
    // predicates: IsItalicSpan (font-style:normal), IsBoldSpan
    // (font-weight:normal), and now IsCenterAligned (text-align:left).
    //
    // The two existing IsCenterAligned siblings
    // (ParentStyleTextAlignCenterTests, SpanOwnStyleTextAlignCenterTests)
    // both pin positive cases — the OR over self+parent fires when either
    // carries `text-align:center`. The negative arm — neither carries
    // center, span has an explicit non-center alignment — was unpinned.
    //
    // The risk this pin uniquely catches and the positive siblings cannot:
    //   • Always-true regression — every positive sibling passes whether
    //     or not the method short-circuits to true.
    //   • Inversion regression (`!Contains` → `Contains`) — positive
    //     inputs still pass under inversion; only negative inputs flip.
    //   • A "less restrictive" refactor (`Contains("text-align")` alone,
    //     dropping the `center` value match) would match `text-align:left`
    //     and promote any left-aligned span to H3.
    //
    // `text-align:left` is production-real on SEC EDGAR's typical body
    // paragraph styles. Asserting `IsCenterAligned == false` on a
    // `text-align:left` span catches both constant-return and
    // value-stripping regressions.
    [Fact]
    public void IsCenterAligned_TextAlignLeft_ReturnsFalse()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span style=\"text-align:left\">Body text</span></body></html>"
            )
            .QuerySelector("span");
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsCenterAligned",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span]);

        result.Should().BeFalse();
    }
}

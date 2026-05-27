using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsApartHalfParenTests
{
    // Sibling to HeadingConversionStepIsApartTests, which pins the positive
    // arm ("(continued)" → true). The half-paren negative path — text that
    // starts with "(" but has no closing ")" — is unpinned.
    //
    // The contract is the conjunction:
    //   StartsWith("(") && EndsWith(")")
    // and the AND-guard is load-bearing. SEC 10-K body text routinely
    // contains open-only fragments like "(footnote 1" continuing across
    // multiple spans, or numeric series with leading parentheses like
    // "(a) the first item ..." where the closing paren lands on a
    // different span. These must NOT be classified as parenthetical
    // asides — otherwise they'd be excluded from heading-match siblings
    // and the heading sweep would silently drop legitimate H3 candidates.
    //
    // The risks this pin uniquely catches:
    //   • Swap `&&` to `||` — the positive arm's "(continued)" still
    //     passes (both sides true), so the existing sibling can't
    //     detect the regression. Half-paren "(footnote" would flip
    //     from false to true, breaking heading classification on a
    //     real SEC text shape.
    //   • Drop the EndsWith side under a "simplify" cleanup. Same
    //     observable: every leading-paren span gets flagged as an aside.
    //
    // Pin: "(footnote" (open-only) — IsApart returns false.
    [Fact]
    public void IsApart_OpenParenWithoutClose_ReturnsFalse()
    {
        var span = new HtmlParser()
            .ParseDocument("<html><body><span>(footnote</span></body></html>")
            .QuerySelector("span");
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsApart",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span]);

        result.Should().BeFalse();
    }
}

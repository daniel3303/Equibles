using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsAllUppercaseMixedCaseTests
{
    // IsAllUppercase decides whether a span should be promoted to <h3> based
    // on "shouting in caps" semantics. Existing sibling pins the letterless
    // case (returns false for "1,234,567"). The mixed-case false-negative
    // path — a span with SOME uppercase letters but not ALL — is unpinned.
    //
    // The risk this pin uniquely catches and the letterless sibling
    // cannot:
    //   • Swap `letters.All(char.IsUpper)` to `letters.Any(char.IsUpper)`.
    //     The letterless sibling passes (0 letters → count > 0 false →
    //     false either way). Mixed input "AbC" has 2 uppercase letters
    //     out of 3 — `Any` returns true, `All` returns false. Under
    //     the `Any` regression, every span with at least one capital
    //     letter would be promoted to H3: company-name camel-case
    //     ("MorganStanley"), proper-noun fragments ("AT&T"), and
    //     mixed-case section descriptions ("Note 1 - Significant
    //     Accounting Policies") would all become spurious headings.
    //   • Swap `letters.All(char.IsUpper)` to `letters.All(char.IsLetter)`
    //     (a less plausible but possible "tidy" regression). All letters
    //     trivially pass IsLetter — the assertion would always be true.
    //
    // Pin: "AbC" (3 letters: A and C upper, b lower) — IsAllUppercase
    // returns false. The mixed shape distinguishes from both `Any` and
    // `IsLetter` regressions.
    [Fact]
    public void IsAllUppercase_MixedCaseLetters_ReturnsFalse()
    {
        var span = new HtmlParser()
            .ParseDocument("<html><body><span>AbC</span></body></html>")
            .QuerySelector("span");
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsAllUppercase",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span]);

        result.Should().BeFalse();
    }
}

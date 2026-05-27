using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsItalicSpanNormalTests
{
    // Existing siblings (IsItalicSpan, IsItalicSpanSpacedColon,
    // IsItalicSpanInnerHtmlTests) all pin the positive arm — a span
    // whose own style or descendant carries `font-style:italic` returns
    // true. The negative arm — a span whose font-style is explicitly
    // something OTHER than italic (the common case `font-style:normal`
    // emitted by SEC EDGAR's CSS reset blocks) — is unpinned.
    //
    // The risk this pin uniquely catches:
    //   • Always-true regression — every positive sibling passes
    //     whether or not the method short-circuits to true. Asserting
    //     false on a non-italic style catches a constant-return
    //     refactor.
    //   • Substring-confusion drift — a future refactor that swapped
    //     the underlying `ContainsCssDeclaration("font-style","italic")`
    //     for `Contains("italic")` would still match `font-style:normal`
    //     (no, that doesn't contain "italic" — but it WOULD match
    //     `font-style-italic-fallback` or any unrelated rule containing
    //     the substring "italic"). The negative pin distinguishes a
    //     working scoped check from a substring-only search by
    //     supplying a `normal` value that is structurally a font-style
    //     declaration but not italic.
    //   • Inversion regression (`!Contains` → `Contains`) — the
    //     positive sibling continues to pass under inversion because
    //     `font-style:italic` always contains the literal. The
    //     negative case `font-style:normal` flips behavior under
    //     inversion and surfaces the regression.
    //
    // Pin: a span with `style="font-style:normal"` — IsItalicSpan
    // returns false.
    [Fact]
    public void IsItalicSpan_FontStyleNormal_ReturnsFalse()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span style=\"font-style:normal\">Body text</span></body></html>"
            )
            .QuerySelector("span");
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItalicSpan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span]);

        result.Should().BeFalse();
    }
}

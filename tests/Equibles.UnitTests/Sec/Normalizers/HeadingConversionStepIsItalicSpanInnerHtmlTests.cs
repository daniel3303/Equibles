using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsItalicSpan's doc-comment promises detection "by either an inline
/// style attribute OR an InnerHtml match." The InnerHtml-fallback arm fires
/// when the italic styling lives on a nested child rather than the span
/// itself — a common SEC pattern where the outer span carries no style and
/// the emphasis tag inside it does. A refactor dropping the nested probe
/// (keeping only the inline-style check pinned elsewhere) would compile
/// cleanly and silently disable h4 promotion for nested-italic headings.
/// </summary>
public class HeadingConversionStepIsItalicSpanInnerHtmlTests
{
    [Fact]
    public void IsItalicSpan_NestedChildWithFontStyleItalic_ReturnsTrue()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span><em style=\"font-style:italic\">Heading</em></span></body></html>"
            )
            .QuerySelector("span")!;
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItalicSpan",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span])!;

        result.Should().BeTrue();
    }
}

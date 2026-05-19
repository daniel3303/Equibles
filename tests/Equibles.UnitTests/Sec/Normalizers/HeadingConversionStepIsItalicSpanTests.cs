using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsItalicSpan detects an italic-styled span by either an inline
/// <c>style="font-style:italic"</c> attribute or an <c>InnerHtml</c> match. It
/// is the per-span discriminator for the h4 promotion path (all-italic
/// siblings). Pinned directly so a refactor that drops one of the two probes
/// (inline vs nested) doesn't silently disable h4 detection.
/// </summary>
public class HeadingConversionStepIsItalicSpanTests
{
    [Fact]
    public void IsItalicSpan_InlineStyleFontStyleItalic_ReturnsTrue()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span style=\"font-style:italic\">Heading</span></body></html>"
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

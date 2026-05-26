using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// Companion to HeadingConversionStepIsCenterAlignedTests, which pins the
/// parent-style probe. IsCenterAligned checks two distinct surfaces, joined by
/// an OR: the span's own inline `text-align:center` style AND the parent
/// element's style. They live in this helper specifically — IsCenterAligned
/// does NOT route through HasInlineCss the way IsBoldSpan / IsItalicSpan do, so
/// a refactor that drops `node.GetAttribute("style")` in this helper compiles,
/// keeps the parent-style pin green, and silently breaks every SEC filing
/// where the centering is declared on the span itself rather than the wrapping
/// div/p. Pin the span-own-style branch explicitly.
/// </summary>
public class HeadingConversionStepIsCenterAlignedSpanOwnStyleTests
{
    [Fact]
    public void IsCenterAligned_SpanOwnStyleTextAlignCenter_ReturnsTrue()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><div><span style=\"text-align:center\">Heading</span></div></body></html>"
            )
            .QuerySelector("span")!;
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsCenterAligned",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span])!;

        result.Should().BeTrue();
    }
}

using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsBoldSpan is one arm of the OR predicate that promotes a span row to
/// &lt;h3&gt;. Pinned directly so a refactor dropping the inline-style probe
/// (and leaving only the InnerHtml fallback) doesn't silently disable h3
/// detection for the common SEC pattern <c>style="font-weight:bold"</c>.
/// </summary>
public class HeadingConversionStepIsBoldSpanTests
{
    [Fact]
    public void IsBoldSpan_InlineStyleFontWeightBold_ReturnsTrue()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span style=\"font-weight:bold\">Section</span></body></html>"
            )
            .QuerySelector("span")!;
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsBoldSpan",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span])!;

        result.Should().BeTrue();
    }
}

using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsCenterAligned is the third arm of the h3 promotion OR (with IsBoldSpan,
/// IsAllUppercase). It checks both the span's own <c>text-align:center</c>
/// inline style and the parent element's style — SEC pages style the wrapping
/// &lt;div&gt; or &lt;p&gt; alone for layout. Pin the parent-style probe
/// specifically, since the inline-on-span case is structurally identical to
/// the IsBoldSpan pin and the parent-fallback would silently break if dropped.
/// </summary>
public class HeadingConversionStepIsCenterAlignedTests
{
    [Fact]
    public void IsCenterAligned_ParentStyleTextAlignCenter_ReturnsTrue()
    {
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><div style=\"text-align:center\"><span>Heading</span></div></body></html>"
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

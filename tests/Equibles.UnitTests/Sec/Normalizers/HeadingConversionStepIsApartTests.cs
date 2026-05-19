using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsApart filters spans that are pure parenthetical asides — "(continued)",
/// "(in millions)" — from the all-siblings heading-match checks. The
/// IsAllUppercase / IsBoldSpan / IsItalicSpan tests cover the positive arms
/// directly, but the IsApart helper itself only fires inside AllSiblingsMatch.
/// Pin its parenthetical-recognition contract so a refactor that drops the
/// EndsWith(')') guard (and starts matching open-only "(footnote") doesn't
/// silently break the heading sweep.
/// </summary>
public class HeadingConversionStepIsApartTests
{
    [Fact]
    public void IsApart_TextWrappedInParentheses_ReturnsTrue()
    {
        var span = new HtmlParser()
            .ParseDocument("<html><body><span>(continued)</span></body></html>")
            .QuerySelector("span")!;
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsApart",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span])!;

        result.Should().BeTrue();
    }
}

using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsItalicSpanSpacedColonTests
{
    [Fact]
    public void IsItalicSpan_InlineStyleFontStyleItalicWithSpaceAfterColon_ReturnsTrue()
    {
        // Sibling to IsItalicSpanTests (compact `font-style:italic`) and to
        // the just-added IsBoldSpan spaced-colon pin (#2372). ContainsCssDeclaration
        // handles both `property:value` and `property: value` because SEC
        // EDGAR ships both forms across filers. A refactor dropping the
        // spaced arm would compile, pass the compact-form pin, and silently
        // miss every italic-span heading from filings that emit the spaced
        // form — h4 promotion would skip them and the rendered markdown
        // would lose its sub-heading anchors.
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span style=\"font-style: italic\">Heading</span></body></html>"
            )
            .QuerySelector("span")!;
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsItalicSpan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method!.Invoke(step, [span])!;

        result.Should().BeTrue();
    }
}

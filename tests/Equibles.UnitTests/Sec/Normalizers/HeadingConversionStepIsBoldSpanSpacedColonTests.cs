using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepIsBoldSpanSpacedColonTests
{
    [Fact]
    public void IsBoldSpan_InlineStyleFontWeightBoldWithSpaceAfterColon_ReturnsTrue()
    {
        // Sibling to IsBoldSpanTests (compact `font-weight:bold`). The
        // ContainsCssDeclaration helper explicitly handles both compact
        // and spaced forms (HeadingConversionStep.cs:179-184) because SEC
        // EDGAR emits CSS both ways across filers. A refactor that drops
        // the `source.Contains($"{property}: {value}")` second arm of the
        // OR (perhaps assuming the upstream serializer normalises spacing)
        // would compile, pass the existing compact-form pin, and silently
        // miss every bold-span heading from filings that ship with the
        // spaced form — h3 promotion would skip them and the rendered
        // markdown would lose its section anchors.
        var span = new HtmlParser()
            .ParseDocument(
                "<html><body><span style=\"font-weight: bold\">Section</span></body></html>"
            )
            .QuerySelector("span")!;
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsBoldSpan",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (bool)method!.Invoke(step, [span])!;

        result.Should().BeTrue();
    }
}

using System.Reflection;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// IsAllUppercase decides whether a span is "shouting in caps" and should
/// therefore be promoted to an &lt;h3&gt; heading. A span with NO letters at all
/// (a stray figure like "1,234,567") is not uppercase text and must not be
/// treated as a heading — otherwise numeric lines corrupt the normalised SEC
/// document structure. Contract derived from the method name and its role as a
/// heading discriminator, before reading the body.
/// </summary>
public class HeadingConversionStepIsAllUppercaseTests
{
    [Fact(
        Skip = "GH-969 — IsAllUppercase returns true for letter-less spans (vacuous All on empty)"
    )]
    public void IsAllUppercase_LetterlessText_ReturnsFalse()
    {
        var span = new HtmlParser()
            .ParseDocument("<html><body><span>1,234,567</span></body></html>")
            .QuerySelector("span")!;
        var method = typeof(HeadingConversionStep).GetMethod(
            "IsAllUppercase",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var step = new HeadingConversionStep();

        var result = (bool)method.Invoke(step, [span])!;

        result
            .Should()
            .BeFalse(
                "a span with no letters is not uppercase text and must not be promoted to a heading"
            );
    }
}

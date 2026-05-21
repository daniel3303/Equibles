using System.Reflection;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepClassifyHeadingTagCascadePrecedenceTests
{
    // ClassifyHeadingTag (extracted in #1573) walks a four-tier cascade:
    // PART → h1, ITEM → h2, bold/uppercase/center-aligned → h3, italic → h4.
    // A span can satisfy multiple tiers (an "Item 1." span is also bold), and
    // the contract picks the higher-precedence tier (h2 over h3). A refactor
    // that reordered the branches — for example to short-circuit the cheap
    // bold check first — would silently demote ITEM headings to h3.
    [Fact]
    public void ClassifyHeadingTag_ItemHeadingThatIsAlsoBold_ReturnsH2NotH3()
    {
        var span = (IElement)
            new HtmlParser()
                .ParseDocument(
                    "<html><body><span style=\"font-weight:bold\">Item 1. Business</span></body></html>"
                )
                .QuerySelector("span");

        var method = typeof(HeadingConversionStep).GetMethod(
            "ClassifyHeadingTag",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (string)method.Invoke(step, ["Item 1. Business", new List<IElement> { span }]);

        result.Should().Be("h2");
    }
}

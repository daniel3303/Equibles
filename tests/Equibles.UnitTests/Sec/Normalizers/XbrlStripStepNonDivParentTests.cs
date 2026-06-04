using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class XbrlStripStepNonDivParentTests
{
    private readonly HtmlParser _parser = new();
    private readonly XbrlStripStep _step = new();

    [Fact]
    public void Execute_IxHeaderWrappedInEmptyNonDivParent_RemovesHeaderButKeepsParent()
    {
        // Contract: the parent-removal in Step 1 is deliberately div-only
        // (`parent.LocalName == "div"`) — it cleans up the hidden iXBRL
        // wrapper <div> that SEC filers emit around ix:header. An empty
        // NON-div parent (here a <span>) must be LEFT in place; only the
        // ix:header itself is removed. The plausible regression: dropping
        // the `LocalName == "div"` guard "to clean up any empty wrapper"
        // would compile, pass every existing div-parent pin, and start
        // deleting arbitrary now-empty ancestors — content loss beyond the
        // intended scope. Derive the oracle from the div-only boundary.
        var doc = _parser.ParseDocument(
            "<html><body><span id=\"wrap\"><ix:header>hidden</ix:header></span><p>keep</p></body></html>"
        );

        _step.Execute(doc);

        var span = doc.GetElementById("wrap");
        span.Should().NotBeNull("a non-div parent must survive even when emptied");
        span!.TextContent.Trim().Should().BeEmpty();
        doc.Body!.InnerHtml.Should().NotContain("ix:header");
        doc.Body.InnerHtml.Should().Contain("<p>keep</p>");
    }
}

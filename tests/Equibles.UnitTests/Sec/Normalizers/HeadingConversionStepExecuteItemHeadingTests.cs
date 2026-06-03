using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepExecuteItemHeadingTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void Execute_BlockWhoseSpanIsAnItemHeading_PromotesItToH2()
    {
        // Contract: the full pipeline (Execute → ClassifyHeadingTag) promotes a block whose span(s)
        // read as an SEC "Item N" heading to an <h2>. Every existing test reflects into the private
        // discriminators; the public Execute (span selection → classify → ReplaceNodeWithHeading)
        // is unit-untested. Drive a real document through it and assert the DOM transformation.
        var doc = _parser.ParseDocument(
            "<html><body><div><span>Item 1.</span></div></body></html>"
        );

        _step.Execute(doc);

        var body = doc.Body!.InnerHtml;
        body.Should().Contain("<h2>");
        body.Should().Contain("Item 1.");
        body.Should().NotContain("<span");
    }
}

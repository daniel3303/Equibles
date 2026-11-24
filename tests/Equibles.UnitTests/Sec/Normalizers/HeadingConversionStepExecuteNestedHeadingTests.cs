using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepExecuteNestedHeadingTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // A block is judged once and not once per span, so a block found to be no heading must be
    // judged again when a heading made inside it takes away the span that kept it from being one.
    [Fact]
    public void Execute_BlockLeftAllBoldByAHeadingMadeInsideIt_IsPromotedToo()
    {
        var doc = _parser.ParseDocument(
            "<html><body><section><div>"
                + "<span style=\"font-weight:bold\">Segment results</span>"
                + "<p><span style=\"font-style:italic\">Unaudited figures</span></p>"
                + "<span style=\"font-weight:bold\">Europe and Asia</span>"
                + "</div></section></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should()
            .Be("<section><h3>Segment resultsUnaudited figuresEurope and Asia</h3></section>");
    }

    // Spans inside a block a heading replaced are out of the document and are skipped; the spans
    // after it are still judged.
    [Fact]
    public void Execute_NestedSpansUnderAHeading_AreSkippedAndLaterBlocksStillJudged()
    {
        var doc = _parser.ParseDocument(
            "<html><body><div><span>Group <span>results <span style=\"font-weight:bold\">2025</span></span></span></div>"
                + "<p><span style=\"font-weight:bold\">Outlook</span></p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().Be("<h3>Group results 2025</h3><h3>Outlook</h3>");
    }
}

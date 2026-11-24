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

    // Judging a block reads all it holds, so blocks past the depth bound are never judged: a chain
    // of spans each holding the rest would otherwise be read once per link.
    [Fact]
    public void Execute_BoldBlockNestedPastTheDepthBound_IsLeftAsText()
    {
        var deep = string.Concat(Enumerable.Repeat("<div>", 70));
        var doc = _parser.ParseDocument(
            "<html><body>"
                + deep
                + "<p><span style=\"font-weight:bold\">Outlook</span></p>"
                + string.Concat(Enumerable.Repeat("</div>", 70))
                + "<div><p><span style=\"font-weight:bold\">Risks</span></p></div></body></html>"
        );

        _step.Execute(doc);

        doc.QuerySelectorAll("h3").Select(heading => heading.TextContent).Should().Equal("Risks");
        doc.Body!.TextContent.Should().Contain("Outlook");
    }

    // A block's spans are each read when it is judged, so a block holding more than the span bound
    // is never judged: spans nested in one another would be read once per span they hold.
    [Fact]
    public void Execute_BoldBlockPastTheSpanBound_IsLeftAsText()
    {
        static string Bold(int count) =>
            string.Concat(
                Enumerable.Repeat("<span style=\"font-weight:bold\">Word </span>", count)
            );
        var doc = _parser.ParseDocument(
            "<html><body><div><p>"
                + Bold(300)
                + "</p></div><div><p>"
                + Bold(10)
                + "</p></div></body></html>"
        );

        _step.Execute(doc);

        doc.QuerySelectorAll("h3")
            .Select(heading => heading.TextContent)
            .Should()
            .Equal(string.Join(" ", Enumerable.Repeat("Word", 10)));
        doc.QuerySelectorAll("p").Should().ContainSingle();
    }
}

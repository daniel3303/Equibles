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
}

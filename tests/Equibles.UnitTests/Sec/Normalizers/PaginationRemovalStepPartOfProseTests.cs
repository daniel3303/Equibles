using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class PaginationRemovalStepPartOfProseTests
{
    private readonly PaginationRemovalStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: the after-HR sibling is deleted only when it is a SEC "Part" section header
    // (the doc-comment says IsPartHeader mirrors HeadingConversionStep.IsPartHeading — i.e.
    // "Part" + a roman-numeral identifier). A prose paragraph that merely begins "Part of …"
    // is body content, so removing it after a page-break <hr> is real content loss and must
    // not happen. Oracle derived from the contract, not the body.
    [Fact(Skip = "GH-3489 — prose paragraph beginning \"Part of …\" after an <hr> is deleted as a Part header")]
    public void Execute_HrFollowedByProseParagraphBeginningPartOf_PreservesParagraph()
    {
        var doc = _parser.ParseDocument(
            "<html><body><hr><p>Part of the proceeds will be reinvested in operations</p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should()
            .Contain("Part of the proceeds will be reinvested in operations");
    }
}

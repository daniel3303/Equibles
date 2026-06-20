using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepPartHeadingEmDashNoSpaceTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: a real SEC "Part" heading is promoted even when its standardized title is
    // glued to the Roman numeral by an em-dash with no surrounding spaces — the canonical
    // SEC 10-Q form "PART II—OTHER INFORMATION". The sibling-test pins the spaced variant
    // ("Part II — Other Information"); SEC EDGAR emits both, and the keyword tokenizer already
    // splits on the ASCII hyphen ("Part II-Other"), so the em-dash (U+2014) it actually emits
    // must tokenize the same way. A missed split leaves the header an un-promoted span,
    // breaking table-of-contents/chunk extraction for that part.
    [Fact(
        Skip = "GH-3866 — em-dash-glued Part heading title not tokenized, header left un-promoted"
    )]
    public void Execute_PartHeadingEmDashNoSpaceTitle_PromotesItToHeading()
    {
        var doc = _parser.ParseDocument(
            "<html><body><div><span>Part II—Other Information</span></div></body></html>"
        );

        _step.Execute(doc);

        var body = doc.Body!.InnerHtml;
        body.Should().Contain("<h1>");
        body.Should().NotContain("<span");
    }
}

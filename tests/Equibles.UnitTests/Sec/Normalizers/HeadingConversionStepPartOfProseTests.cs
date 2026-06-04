using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepPartOfProseTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: the Part→h1 path classifies SEC "PART N" SECTION HEADINGS (per the
    // IsPartHeading docs and existing pins). An ordinary prose sentence that merely
    // begins "Part of …" is body text, not a section heading, so it must NOT be
    // promoted to a heading — doing so corrupts the document outline used by chunking.
    // The span is mixed-case and unstyled, so only the IsPartHeading branch can fire.
    [Fact(Skip = "GH-3431 — prose beginning \"Part of …\" is promoted to an <h1> heading")]
    public void Execute_ProseSentenceBeginningWithPartOf_IsNotPromotedToHeading()
    {
        var doc = _parser.ParseDocument(
            "<html><body><p><span>Part of the proceeds will be reinvested in operations</span></p></body></html>"
        );

        _step.Execute(doc);

        var body = doc.Body!.InnerHtml;
        body.Should().NotContain("<h1");
    }
}

using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepPartRomanProseTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: the Part→h1 path classifies SEC "PART N" SECTION HEADINGS only; prose must
    // never be promoted (the GH-3488 rationale). A cross-reference sentence "Part II of this
    // Annual Report …" is body text even though "II" is a roman numeral.
    // The span is mixed-case and unstyled, so only the IsPartHeading branch can fire.
    [Fact(
        Skip = "GH-3512 — prose sentence beginning \"Part II of …\" is promoted to an h1 heading"
    )]
    public void Execute_ProseSentenceBeginningPartRoman_IsNotPromotedToHeading()
    {
        var doc = _parser.ParseDocument(
            "<html><body><p><span>Part II of this Annual Report on Form 10-K contains forward-looking statements that involve risks and uncertainties</span></p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("<h1");
    }
}

using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepItemDigitProseTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: the Item→h2 path classifies SEC "ITEM N" SECTION HEADINGS only; prose must
    // never be promoted. A cross-reference sentence "Item 1 of this Annual Report …" is body
    // text even though "1" is a valid item identifier (digit sibling of GH-3512's roman case).
    // The span is mixed-case and unstyled, so only the IsItemHeading branch can fire.
    [Fact(Skip = "GH-3514 — prose sentence beginning \"Item 1 of …\" is promoted to an h2 heading")]
    public void Execute_ProseSentenceBeginningItemDigit_IsNotPromotedToHeading()
    {
        var doc = _parser.ParseDocument(
            "<html><body><p><span>Item 1 of this Annual Report contains information about our business and operations that should be read carefully</span></p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("<h2");
    }
}

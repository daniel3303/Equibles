using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepItemOfProseTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Sibling to the Part→h1 case (GH-3431): the Item→h2 path classifies SEC "ITEM N"
    // SECTION HEADINGS, not arbitrary prose. A sentence beginning "Item of …" is body
    // text and must not be promoted to a heading — IsItemHeading only checks for "ITEM"
    // + whitespace + an alphanumeric first token, so an ordinary following word passes.
    // The span is mixed-case and unstyled, so only the IsItemHeading branch can fire.
    [Fact]
    public void Execute_ProseSentenceBeginningWithItemOf_IsNotPromotedToHeading()
    {
        var doc = _parser.ParseDocument(
            "<html><body><p><span>Item of business before the committee was deferred</span></p></body></html>"
        );

        _step.Execute(doc);

        var body = doc.Body!.InnerHtml;
        body.Should().NotContain("<h2");
    }
}

using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class PaginationRemovalStepBarePartTests
{
    // Contract (from the IsPartHeader doc): a Part header is "Part" followed by a
    // whitespace boundary ("Part I"). The bare word "Part" with nothing after it
    // has no following character — no boundary — so it is ordinary content and
    // must be preserved. This pins the `Length > 4` guard, which both rejects the
    // false header AND prevents an out-of-range read of upperText[4] on "PART".
    [Fact]
    public void Execute_HrFollowedByParagraphWithBareWordPart_PreservesParagraph()
    {
        var parser = new HtmlParser();
        var step = new PaginationRemovalStep();
        var doc = parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <hr>
              <p>Part</p>
              <p>Content after</p>
            </body></html>
            """
        );

        step.Execute(doc);

        doc.Body!.InnerHtml.Should().Contain("<p>Part</p>");
    }
}

using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class PaginationRemovalStepPartRomanProseTests
{
    private readonly PaginationRemovalStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: only a SEC "Part" SECTION HEADER after a page-break <hr> is pagination cruft;
    // body prose must never be deleted (content loss — the GH-3489 rationale). A cross-reference
    // sentence "Part II of this Annual Report …" is prose even though "II" is a roman numeral.
    [Fact]
    public void Execute_HrFollowedByProseSentenceBeginningPartRoman_PreservesParagraph()
    {
        var doc = _parser.ParseDocument(
            "<html><body><hr><p>Part II of this Annual Report on Form 10-K contains forward-looking statements that involve risks and uncertainties</p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should()
            .Contain("Part II of this Annual Report on Form 10-K contains forward-looking");
    }
}

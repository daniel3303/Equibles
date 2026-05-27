using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class PaginationRemovalStepHrAloneInBodyTests
{
    // The existing PaginationRemovalStep pins always seed the body with at least
    // one <p> on either side of the <hr>, so FindFirstMeaningfulSibling always
    // finds something and the `return null` at the end of that helper is unhit.
    // A pathological SEC pagination break (or a 10-K cover that ends with a
    // trailing horizontal rule) can leave the <hr> as the only direct child of
    // <body>, with at most whitespace text nodes either side. The pagination
    // step must still remove the <hr> cleanly — a refactor that NRE'd on the
    // null sibling would abort the entire SEC document normalizer pipeline for
    // that filing.
    [Fact]
    public void Execute_HrWithOnlyWhitespaceSiblings_StillRemovesHrWithoutThrowing()
    {
        var parser = new HtmlParser();
        // Whitespace-only text nodes either side of the <hr>: FindFirstMeaningful
        // Sibling walks past them and reaches null in both directions.
        var doc = parser.ParseDocument("<html><body>\n  \n<hr>\n  \n</body></html>");
        var step = new PaginationRemovalStep();

        var act = () => step.Execute(doc);

        act.Should().NotThrow();
        doc.Body!.InnerHtml.Should().NotContain("<hr");
    }
}

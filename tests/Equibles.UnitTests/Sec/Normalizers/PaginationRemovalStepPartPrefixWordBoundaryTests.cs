using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// Sibling to the existing HrWithPartTextAfter_RemovesBoth pin. The intent of
/// the after-hr branch is to detect canonical SEC 10-K Part headers ("Part I",
/// "Part II", …) — the same shape that HeadingConversionStep.IsPartHeading
/// explicitly distinguishes from "Partnership" / "Participants" / "Particular"
/// via a trailing-whitespace word-boundary guard. PaginationRemovalStep applies
/// the looser raw `StartsWith("Part", OrdinalIgnoreCase)` check, so any
/// paragraph beginning with a Part-prefixed word — e.g. "Partnership
/// agreement" introduced after a page-break rule — is misclassified as a Part
/// header and silently destroyed.
/// </summary>
public class PaginationRemovalStepPartPrefixWordBoundaryTests
{
    [Fact]
    public void Execute_HrFollowedByParagraphStartingWithPartPrefixedWord_PreservesParagraph()
    {
        // The paragraph after the <hr> is "Partnership agreement", not a Part
        // section heading. The contract derived from the heading family — that
        // Part-prefixed words must NOT be treated as Part headings — applies
        // here too: this paragraph is body content, not pagination noise, and
        // must survive normalization.
        var parser = new HtmlParser();
        var step = new PaginationRemovalStep();
        var doc = parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <hr>
              <p>Partnership agreement governs the transaction</p>
              <p>Content after</p>
            </body></html>
            """
        );

        step.Execute(doc);

        doc.Body!.InnerHtml.Should()
            .Contain(
                "Partnership",
                "PaginationRemovalStep must apply a word-boundary check after 'Part' to avoid removing paragraphs that merely start with a Part-prefixed word — same rule HeadingConversionStep.IsPartHeading already enforces"
            );
    }
}

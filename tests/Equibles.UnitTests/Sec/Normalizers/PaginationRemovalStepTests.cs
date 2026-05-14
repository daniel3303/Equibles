using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class PaginationRemovalStepTests
{
    private readonly PaginationRemovalStep _step = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void HrWithPageNumberBefore_RemovesBoth()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <p>42</p>
              <hr>
              <p>Content after</p>
            </body></html>
            """
        );

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain("42");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void HrWithPartTextAfter_RemovesBoth()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <hr>
              <p>Part I</p>
              <p>Content after</p>
            </body></html>
            """
        );

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain("Part I");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void HrWithPageNumberBeforeAndPartAfter_RemovesAllThree()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <p>42</p>
              <hr>
              <p>Part II</p>
              <p>Content after</p>
            </body></html>
            """
        );

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain("42");
        bodyHtml.Should().NotContain("Part II");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void HrWithoutPageNumberOrPartText_RemovesOnlyHr()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <hr>
              <p>Content after</p>
            </body></html>
            """
        );

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void NoHrElements_NoChanges()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <p>42</p>
              <p>Part I</p>
              <p>Content after</p>
            </body></html>
            """
        );

        var htmlBefore = doc.Body!.InnerHtml;

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().Be(htmlBefore);
    }

    [Fact]
    public void HrNotDirectChildOfBody_NotAffected()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <div>
                <p>42</p>
                <hr>
                <p>Part I</p>
              </div>
            </body></html>
            """
        );

        var htmlBefore = doc.Body!.InnerHtml;

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().Be(htmlBefore);
    }

    [Fact]
    public void WhitespaceAndCommentsBetweenHrAndPartHeader_StillRemovesPartHeader()
    {
        // Sibling pin to WhitespaceAndCommentsBetweenHrAndPageNumber_StillRemoved.
        // That test covers the BEFORE-HR skip-loop (comments/whitespace between an
        // <hr> and the candidate page-number sibling). This pin covers the AFTER-HR
        // skip-loop — structurally distinct, in a separate `while (nextSibling != null)`
        // block inside PaginationRemovalStep.Execute.
        //
        // SEC 10-K HTML routinely interleaves `<!-- page break -->` comments and
        // whitespace text nodes between page-separator `<hr>` and the following
        // Part header — the upstream EDGAR-to-XBRL converters insert them as
        // pagination annotations. The skip-loop on the after-side must walk past
        // those non-substantive nodes to find the "Part I" / "Part II" textual
        // sibling and add it to the elements-to-remove list. Without the skip-loop
        // (or with it accidentally inverted), the loop's break would fire on the
        // comment node, the Part header would not be detected, and the chunker
        // downstream would treat the Part heading as continuation of the previous
        // section — wrecking heading-based document outline extraction.
        //
        // Pin: an <hr> followed by a comment, then a `<p>Part III</p>`. Assert that
        // Part III is removed (proving the skip-loop walked past the comment to
        // find and recognize it) and that surrounding content survives.
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <hr>
              <!-- page break -->
              <p>Part III</p>
              <p>Content after</p>
            </body></html>
            """
        );

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain("Part III");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void WhitespaceAndCommentsBetweenHrAndPageNumber_StillRemoved()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
              <p>Content before</p>
              <p>7</p>
              <!-- page break -->
              <hr>
              <p>Content after</p>
            </body></html>
            """
        );

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain(">7<");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }
}

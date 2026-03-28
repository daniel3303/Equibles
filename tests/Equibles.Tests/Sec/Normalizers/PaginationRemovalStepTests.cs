using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.Tests.Sec.Normalizers;

public class PaginationRemovalStepTests {
    private readonly PaginationRemovalStep _step = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void HrWithPageNumberBefore_RemovesBoth() {
        var doc = _parser.ParseDocument("""
            <html><body>
              <p>Content before</p>
              <p>42</p>
              <hr>
              <p>Content after</p>
            </body></html>
            """);

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain("42");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void HrWithPartTextAfter_RemovesBoth() {
        var doc = _parser.ParseDocument("""
            <html><body>
              <p>Content before</p>
              <hr>
              <p>Part I</p>
              <p>Content after</p>
            </body></html>
            """);

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain("Part I");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void HrWithPageNumberBeforeAndPartAfter_RemovesAllThree() {
        var doc = _parser.ParseDocument("""
            <html><body>
              <p>Content before</p>
              <p>42</p>
              <hr>
              <p>Part II</p>
              <p>Content after</p>
            </body></html>
            """);

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain("42");
        bodyHtml.Should().NotContain("Part II");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void HrWithoutPageNumberOrPartText_RemovesOnlyHr() {
        var doc = _parser.ParseDocument("""
            <html><body>
              <p>Content before</p>
              <hr>
              <p>Content after</p>
            </body></html>
            """);

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }

    [Fact]
    public void NoHrElements_NoChanges() {
        var doc = _parser.ParseDocument("""
            <html><body>
              <p>Content before</p>
              <p>42</p>
              <p>Part I</p>
              <p>Content after</p>
            </body></html>
            """);

        var htmlBefore = doc.Body!.InnerHtml;

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().Be(htmlBefore);
    }

    [Fact]
    public void HrNotDirectChildOfBody_NotAffected() {
        var doc = _parser.ParseDocument("""
            <html><body>
              <div>
                <p>42</p>
                <hr>
                <p>Part I</p>
              </div>
            </body></html>
            """);

        var htmlBefore = doc.Body!.InnerHtml;

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().Be(htmlBefore);
    }

    [Fact]
    public void WhitespaceAndCommentsBetweenHrAndPageNumber_StillRemoved() {
        var doc = _parser.ParseDocument("""
            <html><body>
              <p>Content before</p>
              <p>7</p>
              <!-- page break -->
              <hr>
              <p>Content after</p>
            </body></html>
            """);

        _step.Execute(doc);

        var bodyHtml = doc.Body!.InnerHtml;
        bodyHtml.Should().NotContain("<hr");
        bodyHtml.Should().NotContain(">7<");
        bodyHtml.Should().Contain("Content before");
        bodyHtml.Should().Contain("Content after");
    }
}

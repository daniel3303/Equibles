using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class XbrlStripStepTests
{
    private readonly HtmlParser _parser = new();
    private readonly XbrlStripStep _step = new();

    [Fact]
    public void Execute_RemovesIxHeaderElement()
    {
        var doc = _parser.ParseDocument(
            "<html><body><ix:header>hidden XBRL context</ix:header><p>visible</p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("ix:header");
        doc.Body.InnerHtml.Should().NotContain("hidden XBRL context");
        doc.Body.InnerHtml.Should().Contain("<p>visible</p>");
    }

    [Fact]
    public void Execute_RemovesEmptyParentDivOfIxHeader()
    {
        var doc = _parser.ParseDocument(
            "<html><body><div><ix:header>hidden</ix:header></div><p>keep</p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Trim().Should().Be("<p>keep</p>");
    }

    [Fact]
    public void Execute_RemovesDeiNamespacedElements()
    {
        var doc = _parser.ParseDocument(
            "<html><body><dei:entityRegistrantName>Acme Corp</dei:entityRegistrantName><p>content</p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("dei:");
        doc.Body.InnerHtml.Should().NotContain("Acme Corp");
        doc.Body.InnerHtml.Should().Contain("<p>content</p>");
    }

    [Fact]
    public void Execute_RemovesXbrliElements()
    {
        var doc = _parser.ParseDocument(
            "<html><body><xbrli:context id=\"c1\">context data</xbrli:context><p>visible</p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("xbrli:");
        doc.Body.InnerHtml.Should().NotContain("context data");
        doc.Body.InnerHtml.Should().Contain("<p>visible</p>");
    }

    [Fact]
    public void Execute_UnwrapsIxNonFractionPreservingText()
    {
        var doc = _parser.ParseDocument(
            "<html><body><p>Revenue: <ix:nonfraction name=\"us-gaap:Revenue\">1,234,567</ix:nonfraction></p></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("ix:nonfraction");
        doc.Body.InnerHtml.Should().Contain("1,234,567");
        doc.Body.InnerHtml.Should().Contain("<p>");
    }

    [Fact]
    public void Execute_UnwrapsIxNonNumericPreservingChildren()
    {
        var doc = _parser.ParseDocument(
            "<html><body><ix:nonNumeric name=\"us-gaap:Note\"><span>Note text</span><em>emphasis</em></ix:nonNumeric></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("ix:nonnumeric");
        doc.Body.InnerHtml.Should().Contain("<span>Note text</span>");
        doc.Body.InnerHtml.Should().Contain("<em>emphasis</em>");
    }

    [Fact]
    public void Execute_DivContainingIxHeaderAndSubstantiveContent_KeepsDivAndRemovesOnlyHeader()
    {
        // The sibling `Execute_RemovesEmptyParentDivOfIxHeader` test covers the empty-
        // parent-div cleanup branch — when the div wraps ONLY the XBRL header and
        // becomes whitespace-only after the header is removed, the div is dropped too.
        // What that test does NOT pin is the opposite branch: when the parent div has
        // genuine, human-readable content alongside the header, the div must STAY and
        // only the ix:header is removed. The decision hinges on the
        // `string.IsNullOrWhiteSpace(parent.TextContent)` guard. A refactor that drops
        // that guard (or inverts it, or replaces it with a structural-children check
        // like `parent.ChildElementCount == 0`) would compile cleanly, pass the
        // empty-parent sibling, and then silently strip out every SEC filing div that
        // happens to wrap both a header and the actual filing body — which is the
        // common case for inline-XBRL 10-K layouts. Pin the preservation branch so the
        // regression surfaces here rather than as a SEC filing with its body missing.
        var doc = _parser.ParseDocument(
            "<html><body><div><ix:header>hidden XBRL context</ix:header><p>Revenue increased 12%</p></div></body></html>"
        );

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().NotContain("ix:header");
        doc.Body.InnerHtml.Should().NotContain("hidden XBRL context");
        doc.Body.InnerHtml.Should().Contain("<div>");
        doc.Body.InnerHtml.Should().Contain("<p>Revenue increased 12%</p>");
    }

    [Fact]
    public void Execute_LeavesRegularHtmlElementsUntouched()
    {
        const string html =
            "<html><body><div><h1>Title</h1><p>Paragraph</p><table><tr><td>Cell</td></tr></table></div></body></html>";
        var doc = _parser.ParseDocument(html);

        _step.Execute(doc);

        doc.Body!.InnerHtml.Should().Contain("<h1>Title</h1>");
        doc.Body.InnerHtml.Should().Contain("<p>Paragraph</p>");
        doc.Body.InnerHtml.Should().Contain("<table>");
        doc.Body.InnerHtml.Should().Contain("<td>Cell</td>");
    }

    [Fact]
    public void Execute_EmptyDocument_DoesNotThrow()
    {
        var doc = _parser.ParseDocument("<html><body></body></html>");

        var act = () => _step.Execute(doc);

        act.Should().NotThrow();
    }
}

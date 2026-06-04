using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class ListConversionStepAnchorOnlyItemTests
{
    private readonly ListConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void Execute_ItemWithoutInlineContentDiv_PreservesAnchorInsteadOfEmptyLi()
    {
        // Contract: when a list item has NO `<div style="display:inline">` content
        // wrapper, the converter must fall back to the item's whole content so an
        // anchor-only entry (SEC exhibit-index links) survives in the <li>. This is
        // the exact regression the source comment documents: an earlier version that
        // cloned only <span> children produced an empty <li> and dropped the reference.
        // Oracle derived from the doc-comment, before reading the body.
        var html =
            "<html><body>"
            + "<div class=\"item-list-element-wrapper\"><span>•</span>"
            + "<a href=\"ex10-1.htm\">Exhibit 10.1</a></div>"
            + "</body></html>";
        var doc = _parser.ParseDocument(html);

        _step.Execute(doc);

        var result = doc.Body!.InnerHtml;
        result.Should().Contain("<ul>");
        result.Should().Contain("<li>");
        result.Should().Contain("Exhibit 10.1");
        result.Should().Contain("ex10-1.htm");
        result.Should().NotContain("•");
        result.Should().NotContain("item-list-element-wrapper");
    }
}

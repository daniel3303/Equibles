using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class ListConversionStepNestedBulletTests
{
    private readonly ListConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    private string Execute(string bodyHtml)
    {
        var doc = _parser.ParseDocument($"<html><body>{bodyHtml}</body></html>");
        _step.Execute(doc);
        return doc.Body!.InnerHtml;
    }

    // Combination the existing bullet pins miss: the bullet span sits INSIDE the inline
    // content div, not as a sibling before it. The bullet matcher is a descendant query,
    // so the glyph must still be stripped — and because removal happens before the content
    // div's InnerHtml is lifted into the <li>, the rendered item must keep its text and
    // drop the decorative bullet rather than baking "•" into the persisted document.
    [Fact]
    public void Execute_BulletSpanNestedInsideInlineContentDiv_StripsBulletKeepsText()
    {
        var input = """
            <div class="item-list-element-wrapper">
              <div style="display:inline"><span>•</span>Item with nested bullet</div>
            </div>
            """;

        var result = Execute(input);

        result.Should().Contain("<li>");
        result.Should().Contain("Item with nested bullet");
        result.Should().NotContain("•");
    }
}

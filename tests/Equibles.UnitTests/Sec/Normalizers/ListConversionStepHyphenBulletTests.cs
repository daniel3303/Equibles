using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class ListConversionStepHyphenBulletTests
{
    private readonly ListConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Third sibling in the bullet-matcher family. ConvertItemToListItem's
    // bullet glyph allowlist at line 95 is the three-arm pattern
    //   `s.TextContent.Trim() is "•" or "·" or "-"`
    // BulletSpans_AreRemovedFromListItems pins "•"; MiddleDotBulletSpans
    // pins "·". The hyphen-minus arm is unpinned. SEC filings emitted by
    // older ASCII-only pipelines (some 10-K exhibit indices, plain-text
    // 10-D filings) use "-" as the bullet glyph; without this arm the
    // hyphen survives into the rendered <li> and shows up as a stray "-"
    // prefix in the public document viewer. A refactor that "rationalises"
    // the three-arm pattern down to the dominant Unicode bullets would
    // compile, pass the two existing bullet tests, and silently regress
    // every hyphen-bullet filing.
    [Fact]
    public void HyphenBulletSpans_AreRemovedFromListItems()
    {
        var input = """
            <div class="item-list-element-wrapper">
              <span>-</span>
              <div style="display:inline">Item with hyphen bullet</div>
            </div>
            """;

        var html = $"<html><body>{input}</body></html>";
        var doc = _parser.ParseDocument(html);
        _step.Execute(doc);
        var result = doc.Body!.InnerHtml;

        result.Should().Contain("<ul>");
        result.Should().Contain("<li>");
        result.Should().Contain("Item with hyphen bullet");
        // The hyphen-bullet span must not survive into the rendered li.
        result.Should().NotContain("<span>-</span>");
    }
}

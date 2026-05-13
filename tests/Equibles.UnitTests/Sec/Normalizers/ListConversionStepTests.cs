using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class ListConversionStepTests {
    private readonly ListConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    private string Execute(string bodyHtml) {
        var html = $"<html><body>{bodyHtml}</body></html>";
        var doc = _parser.ParseDocument(html);
        _step.Execute(doc);
        return doc.Body!.InnerHtml;
    }

    [Fact]
    public void SingleItemWrapper_ConvertsToUlWithOneLi() {
        var input = """
            <div class="item-list-element-wrapper">
              <span>•</span>
              <div style="display:inline">First item content</div>
            </div>
            """;

        var result = Execute(input);

        result.Should().Contain("<ul>");
        result.Should().Contain("<li>");
        result.Should().Contain("First item content");
        result.Should().NotContain("item-list-element-wrapper");
    }

    [Fact]
    public void MultipleConsecutiveWrappers_ConvertToSingleUlWithMultipleLi() {
        var input = """
            <div class="item-list-element-wrapper">
              <span>•</span>
              <div style="display:inline">First item content</div>
            </div>
            <div class="item-list-element-wrapper">
              <span>•</span>
              <div style="display:inline">Second item content</div>
            </div>
            """;

        var result = Execute(input);

        var ulCount = System.Text.RegularExpressions.Regex.Matches(result, "<ul>").Count;
        var liCount = System.Text.RegularExpressions.Regex.Matches(result, "<li>").Count;

        ulCount.Should().Be(1);
        liCount.Should().Be(2);
        result.Should().Contain("First item content");
        result.Should().Contain("Second item content");
    }

    [Fact]
    public void BulletSpans_AreRemovedFromListItems() {
        var input = """
            <div class="item-list-element-wrapper">
              <span>•</span>
              <div style="display:inline">Item with bullet</div>
            </div>
            """;

        var result = Execute(input);

        result.Should().NotContain("•");
        result.Should().Contain("Item with bullet");
    }

    [Fact]
    public void MiddleDotBulletSpans_AreRemovedFromListItems() {
        // ConvertItemToListItem's bullet matcher is the three-arm pattern
        //   `s.TextContent.Trim() is "•" or "·" or "-"`
        // The existing `BulletSpans_AreRemovedFromListItems` test only
        // exercises the "•" (U+2022 BULLET) arm. The middle-arm "·"
        // (U+00B7 MIDDLE DOT) is unpinned and structurally distinct: it's
        // the *second* visually-similar bullet character that SEC filers
        // routinely use, especially in proxy statements rendered from
        // Donnelley Financial and DFS pipelines. A regression that
        // "consolidates" the three-arm pattern to just `is "•"` (the
        // dominant case) would compile, pass the existing bullet test,
        // and silently leave every `·` in the rendered list items — a
        // visible glyph baked into the persisted document text that
        // would show up in the public document viewer and in MCP
        // `SearchDocumentKeyword` results.
        //
        // The risk is asymmetric vs. the existing `•` pin: the `•` arm
        // is the dominant case and would never be dropped accidentally;
        // the `·` arm is the one that goes quiet first when someone
        // "simplifies" the pattern. Pin it explicitly so the
        // visually-similar-but-encoded-differently bullet survives.
        //
        // The hyphen `-` arm is the third (also unpinned) but is more
        // ambiguous — hyphens legitimately appear inside list-item text
        // ("non-GAAP", "Q4-2024"). The middle-dot arm is the cleaner pin
        // because `·` is unambiguously decorative in this context.
        var input = """
            <div class="item-list-element-wrapper">
              <span>·</span>
              <div style="display:inline">Item with middle-dot bullet</div>
            </div>
            """;

        var result = Execute(input);

        result.Should().NotContain("·");
        result.Should().Contain("Item with middle-dot bullet");
    }

    [Fact]
    public void ContentFromInlineDisplayDiv_IsPreservedInLi() {
        var input = """
            <div class="item-list-element-wrapper">
              <span>-</span>
              <div style="display:inline">Preserved <strong>bold</strong> content</div>
            </div>
            """;

        var result = Execute(input);

        result.Should().Contain("<li>");
        result.Should().Contain("Preserved <strong>bold</strong> content");
    }

    [Fact]
    public void NonConsecutiveWrappers_BecomeSeparateLists() {
        var input = """
            <div class="item-list-element-wrapper">
              <span>•</span>
              <div style="display:inline">First list item</div>
            </div>
            <p>Separator paragraph</p>
            <div class="item-list-element-wrapper">
              <span>•</span>
              <div style="display:inline">Second list item</div>
            </div>
            """;

        var result = Execute(input);

        var ulCount = System.Text.RegularExpressions.Regex.Matches(result, "<ul>").Count;
        ulCount.Should().Be(2);
        result.Should().Contain("Separator paragraph");
    }

    [Fact]
    public void NoItemWrappers_NoChanges() {
        var input = "<p>Regular paragraph content</p>";

        var result = Execute(input);

        result.Should().Contain("<p>Regular paragraph content</p>");
        result.Should().NotContain("<ul>");
    }

    [Fact]
    public void WrapperWithoutInlineDisplayDiv_FallsBackToCloningContentSpans() {
        // When the item wrapper has no <div style="display:inline"> content
        // container, the converter falls back to cloning the non-bullet
        // spans into the <li>. This pins the fallback path so SEC filings
        // that put item content directly in spans still produce a list.
        var input = """
            <div class="item-list-element-wrapper">
              <span>•</span>
              <span>Span-based item</span>
            </div>
            """;

        var result = Execute(input);

        result.Should().Contain("<ul>");
        result.Should().Contain("<li>");
        result.Should().Contain("<span>Span-based item</span>");
        result.Should().NotContain("•");
    }

    [Fact]
    public void EmptyDocument_NoError() {
        var result = Execute(string.Empty);

        result.Should().NotBeNull();
    }
}

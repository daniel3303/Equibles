using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.Tests.Sec.Normalizers;

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
    public void EmptyDocument_NoError() {
        var result = Execute(string.Empty);

        result.Should().NotBeNull();
    }
}

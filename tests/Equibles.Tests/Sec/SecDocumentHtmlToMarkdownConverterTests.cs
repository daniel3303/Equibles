using Equibles.Sec.BusinessLogic;

namespace Equibles.Tests.Sec;

public class SecDocumentHtmlToMarkdownConverterTests {
    private readonly SecDocumentHtmlToMarkdownConverter _converter = new();

    [Fact]
    public void Convert_NullInput_ReturnsEmptyString() {
        var result = _converter.Convert(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Convert_EmptyString_ReturnsEmptyString() {
        var result = _converter.Convert("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Convert_SimpleParagraph_ContainsText() {
        var result = _converter.Convert("<p>Hello world</p>");

        result.Should().Contain("Hello world");
    }

    [Fact]
    public void Convert_BoldText_ProducesMarkdownBold() {
        var result = _converter.Convert("<strong>bold</strong>");

        result.Should().Contain("**bold**");
    }

    [Fact]
    public void Convert_Header_ProducesMarkdownHeader() {
        var result = _converter.Convert("<h1>Title</h1>");

        result.Should().Contain("# Title");
    }

    [Fact]
    public void Convert_DuplicateStyleAttributes_DoesNotThrow() {
        var html = """<p style="font-weight:bold;font-weight:bold">styled text</p>""";

        var act = () => _converter.Convert(html);

        act.Should().NotThrow();
        act().Should().Contain("styled text");
    }

    [Fact]
    public void Convert_HtmlTable_ProducesPipeTableWithBlankLines() {
        var html = """
            <p>Before table</p>
            <table>
                <thead>
                    <tr><th>Name</th><th>Value</th></tr>
                </thead>
                <tbody>
                    <tr><td>Alpha</td><td>100</td></tr>
                </tbody>
            </table>
            <p>After table</p>
            """;

        var result = _converter.Convert(html);

        result.Should().Contain("|");
        result.Should().Contain("Name");
        result.Should().Contain("Alpha");

        // Verify blank line before pipe table
        result.Should().MatchRegex(@"\n\n\|");
        // Verify blank line after pipe table
        result.Should().MatchRegex(@"\|\n\n");
    }
}

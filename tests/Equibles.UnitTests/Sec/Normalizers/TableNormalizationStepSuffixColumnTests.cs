using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepSuffixColumnTests
{
    private static List<List<string>> Normalize(string tableHtml)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument($"<html><body>{tableHtml}</body></html>");
        new TableNormalizationStep(parser).Execute(doc);
        return doc.QuerySelectorAll("tr")
            .Select(row => row.QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToList())
            .ToList();
    }

    [Fact]
    public void Execute_NegativeWithItsClosingParenthesisInTheNextColumn_ReadsAsOneFigure()
    {
        // Fidelity's statement of operations: a negative opens in one cell and closes in the next.
        var rows = Normalize(
            "<table>"
                + "<tr><td>Net Investment Income (Loss)</td><td>(8,036</td><td>)</td><td>(12,633</td><td>)</td></tr>"
                + "<tr><td>Net realized gain (loss)</td><td>(68,471</td><td>)</td><td>2,432</td><td></td></tr>"
                + "</table>"
        );

        rows[0].Should().Equal("Net Investment Income (Loss)", "(8,036)", "(12,633)");
        rows[1].Should().Equal("Net realized gain (loss)", "(68,471)", "2,432");
    }

    [Fact]
    public void Execute_PercentInItsOwnColumn_JoinsTheFigure()
    {
        var rows = Normalize(
            "<table>"
                + "<tr><td>Compute &amp; Networking</td><td>77,286</td><td>67</td><td>%</td></tr>"
                + "<tr><td>Graphics</td><td>8,155</td><td>(3</td><td>)%</td></tr>"
                + "</table>"
        );

        rows[0].Should().Equal("Compute & Networking", "77,286", "67%");
        rows[1].Should().Equal("Graphics", "8,155", "(3)%");
    }

    [Fact]
    public void Execute_PercentAfterAClosedNegative_JoinsTheFigure()
    {
        // Apple's net sales table: a change already closed as "(7)" with its percent alongside.
        var rows = Normalize(
            "<table>"
                + "<tr><td>iPhone</td><td>23</td><td>%</td></tr>"
                + "<tr><td>Mac</td><td>(7)</td><td>%</td></tr>"
                + "</table>"
        );

        rows[0].Should().Equal("iPhone", "23%");
        rows[1].Should().Equal("Mac", "(7)%");
    }

    [Fact]
    public void Execute_ClosedNegativeFollowedByAnotherParenthesis_IsLeftAlone()
    {
        var rows = Normalize("<table><tr><td>Item</td><td>(7)</td><td>)</td></tr></table>");

        rows[0].Should().Equal("Item", "(7)", ")");
    }

    [Fact]
    public void Execute_SuffixColumnThatAlsoCarriesText_IsLeftAlone()
    {
        var rows = Normalize(
            "<table>"
                + "<tr><td>Item</td><td>(8,036</td><td>)</td></tr>"
                + "<tr><td>Note</td><td>12</td><td>see below</td></tr>"
                + "</table>"
        );

        rows[0].Should().Equal("Item", "(8,036", ")");
        rows[1].Should().Equal("Note", "12", "see below");
    }

    [Fact]
    public void Execute_ParenthesisAfterACellWithoutAFigure_IsLeftAlone()
    {
        var rows = Normalize(
            "<table><tr><td>Total (unaudited</td><td>)</td><td>5</td></tr></table>"
        );

        rows[0].Should().Equal("Total (unaudited", ")", "5");
    }

    [Fact]
    public void Execute_HeaderColumnsAboveTheSuffixColumnStayAligned()
    {
        // The period header spans figure and suffix; after the merge each header sits over its figure.
        var rows = Normalize(
            "<table>"
                + "<tr><td></td><td colspan=\"2\">2026</td><td colspan=\"2\">2025</td></tr>"
                + "<tr><td>Net loss</td><td>(1,585,851</td><td>)</td><td>(10</td><td>)</td></tr>"
                + "</table>"
        );

        rows[0].Should().Equal("", "2026", "2025");
        rows[1].Should().Equal("Net loss", "(1,585,851)", "(10)");
    }

    [Fact]
    public void NormalizeAndConvert_SplitNegative_RendersWholeInMarkdown()
    {
        var html =
            "<table>"
            + "<tr><td>Net Investment Income (Loss)</td><td>(8,036</td><td>)</td><td>(12,633</td><td>)</td></tr>"
            + "<tr><td>Net realized gain</td><td>2,432</td><td></td><td>(235,837</td><td>)</td></tr>"
            + "</table>";

        var markdown = new SecDocumentHtmlToMarkdownConverter().Convert(
            new SecDocumentHtmlNormalizer().NormalizeFragment(html)
        );

        markdown.Should().Contain("| (8,036) | (12,633) |").And.Contain("| 2,432 | (235,837) |");
        markdown.Should().NotContain("| ) |");
    }
}

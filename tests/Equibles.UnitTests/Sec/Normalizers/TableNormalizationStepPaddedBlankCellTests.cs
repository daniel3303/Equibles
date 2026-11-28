using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class TableNormalizationStepPaddedBlankCellTests
{
    private readonly HtmlParser _parser = new(
        new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
    );

    [Theory]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<font>&nbsp;</font>")]
    [InlineData("<div><p><span>\u200B</span></p></div>")]
    [InlineData("<p>&nbsp;&nbsp;\u200B&nbsp;\u200B</p>")]
    public void Execute_SpacerCellsPaddedByWrapperOrZeroWidthSpace_RemovesSpacerRowAndColumn(
        string padding
    )
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + $"<tr><td>Revenue</td><td>{padding}</td><td>1,234</td></tr>"
                + $"<tr><td>{padding}</td><td>{padding}</td><td>{padding}</td></tr>"
                + $"<tr><td>Cost</td><td>{padding}</td><td>567</td></tr>"
                + "</table></body></html>"
        );

        new TableNormalizationStep().Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Select(row => row.TextContent).Should().Equal("Revenue1,234", "Cost567");
    }

    [Fact]
    public void Execute_KeptBlankCell_LosesItsPadding()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td><p>\u200B</p></td><td>2026</td></tr>"
                + "<tr><td>Revenue</td><td>1,234</td></tr>"
                + "</table></body></html>"
        );

        new TableNormalizationStep().Execute(doc);

        doc.QuerySelector("td")!.InnerHtml.Should().BeEmpty();
    }

    [Fact]
    public void Execute_LabelWrappedAcrossSourceLines_ReadsAsOneLine()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Cash\nFlows From Operating\r\n   Activities:</td><td>1,234</td></tr>"
                + "</table></body></html>"
        );

        new TableNormalizationStep().Execute(doc);

        doc.QuerySelector("td")!.TextContent.Should().Be("Cash Flows From Operating Activities:");
    }
}

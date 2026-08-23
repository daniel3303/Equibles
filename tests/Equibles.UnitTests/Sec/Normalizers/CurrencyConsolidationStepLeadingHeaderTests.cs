using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepLeadingHeaderTests
{
    [Fact]
    public void Execute_LeadingPeriodHeaderInCurrencyColumn_ShiftsHeaderAndRemovesWholeColumn()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Jan 25, 2026</td><td></td><td>Jan 26, 2025</td><td></td></tr>"
                + "<tr><td>$</td><td>193,479</td><td>$</td><td>116,193</td></tr>"
                + "<tr><td></td><td>22,459</td><td></td><td>14,304</td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Should().AllSatisfy(row => row.QuerySelectorAll("td").Should().HaveCount(2));
        rows[0]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent.Trim())
            .Should()
            .Equal("Jan 25, 2026", "Jan 26, 2025");
        rows[1]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent.Trim())
            .Should()
            .Equal("193,479", "116,193");
        rows[2]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent.Trim())
            .Should()
            .Equal("22,459", "14,304");
    }

    [Fact]
    public void Execute_MixedForeignDollarSymbol_PreservesColumnAndDoesNotAddUsdNote()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>$</td><td>100</td></tr>"
                + "<tr><td>C$</td><td></td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Should().AllSatisfy(row => row.QuerySelectorAll("td").Should().HaveCount(2));
        rows[0].QuerySelectorAll("td")[0].TextContent.Should().Be("$");
        rows[1].QuerySelectorAll("td")[0].TextContent.Should().Be("C$");
        doc.Body.TextContent.Should().NotContain("All values are in US Dollars");
    }

    [Fact]
    public void Execute_SeparateUsdAndForeignDollarColumns_DoesNotAddTableWideUsdNote()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>$</td><td>100</td><td>C$</td><td></td></tr>"
                + "<tr><td>$</td><td>200</td><td>C$</td><td></td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Should().AllSatisfy(row => row.QuerySelectorAll("td").Should().HaveCount(3));
        rows.Should()
            .AllSatisfy(row => row.QuerySelectorAll("td")[1].TextContent.Should().Be("C$"));
        doc.Body.TextContent.Should().NotContain("All values are in US Dollars");
    }
}

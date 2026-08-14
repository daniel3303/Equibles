using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepPopulatedValueColumnTests
{
    [Fact]
    public void Execute_CurrencyColumnPrecedesPopulatedValues_RemovesColumnFromEveryRow()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Compute &amp; Networking</td><td>$</td><td>193,479</td><td>$</td><td>116,193</td><td>67%</td></tr>"
                + "<tr><td>Graphics</td><td></td><td>22,459</td><td></td><td>14,304</td><td>57%</td></tr>"
                + "<tr><td>Total</td><td>$</td><td>215,938</td><td>$</td><td>130,497</td><td>65%</td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows.Should().AllSatisfy(row => row.QuerySelectorAll("td").Should().HaveCount(4));
        rows[0]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent.Trim())
            .Should()
            .Equal("Compute & Networking", "193,479", "116,193", "67%");
        rows[1]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent.Trim())
            .Should()
            .Equal("Graphics", "22,459", "14,304", "57%");
        rows[2]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent.Trim())
            .Should()
            .Equal("Total", "215,938", "130,497", "65%");
    }

    [Fact]
    public void Execute_CurrencyScaleHeaderPrecedesYear_PreservesScaleText()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>USD in millions</td><td>2026</td><td>2025</td></tr>"
                + "<tr><td>$</td><td>215,938</td><td>130,497</td></tr>"
                + "</table></body></html>"
        );

        step.Execute(doc);

        doc.QuerySelectorAll("tr").Should().AllSatisfy(row => row.Children.Should().HaveCount(2));
        doc.QuerySelector("tr td").TextContent.Trim().Should().Be("in millions 2026");
    }

    [Fact]
    public void Execute_MixedCurrencyAndNonCurrencyCandidate_LeavesTableWithoutCurrencyNote()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>$</td><td>100</td></tr>"
                + "<tr><td>subtotal</td><td>200</td></tr>"
                + "</table></body></html>"
        );
        var originalTable = doc.QuerySelector("table").OuterHtml;

        step.Execute(doc);

        doc.QuerySelector("table").OuterHtml.Should().Be(originalTable);
        doc.QuerySelector("table + p").Should().BeNull();
    }

    [Fact]
    public void Execute_AdjacentCurrencyPrefixedValues_LeavesPeriodColumnsUnchanged()
    {
        var parser = new HtmlParser();
        var step = new CurrencyConsolidationStep();
        var doc = parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Revenue</td><td>$100</td><td>$200</td></tr>"
                + "<tr><td>Expenses</td><td>$60</td><td>$80</td></tr>"
                + "</table></body></html>"
        );
        var originalTable = doc.QuerySelector("table").OuterHtml;

        step.Execute(doc);

        doc.QuerySelector("table").OuterHtml.Should().Be(originalTable);
        doc.QuerySelector("table + p").Should().BeNull();
    }
}

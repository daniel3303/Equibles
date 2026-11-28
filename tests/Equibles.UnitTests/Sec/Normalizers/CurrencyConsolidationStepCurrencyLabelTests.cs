using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

// Labels and headers that mention a currency are words, not a currency column's sign: before, a
// label column beside an empty one was consolidated as a currency column and lost its symbols.
public class CurrencyConsolidationStepCurrencyLabelTests
{
    private readonly HtmlParser _parser = new();

    [Theory]
    [InlineData("Issuance of common stock, net of $437 issuance cost")]
    [InlineData("Common stock: $0.0001 par value, 500,000,000 authorized")]
    [InlineData("Year-end US$:S$ exchange rate")]
    public void Execute_LabelMentioningACurrencyBesideAnEmptyColumn_KeepsLabelAndAddsNoNote(
        string label
    )
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + $"<tr><td>{label}</td><td></td><td>3,773</td></tr>"
                + "<tr><td>Balance</td><td></td><td>5</td></tr>"
                + "</table></body></html>"
        );

        new CurrencyConsolidationStep().Execute(doc);

        doc.QuerySelectorAll("td").Select(cell => cell.TextContent).Should().Contain(label);
        doc.QuerySelector("table + p em").Should().BeNull();
    }

    [Fact]
    public void Execute_HeaderNamingUsDollarsInASignColumn_ShiftsHeaderWithItsCurrency()
    {
        var doc = _parser.ParseDocument(
            "<html><body><table>"
                + "<tr><td>Use of Proceeds</td><td>Initial Estimated Amount (US$)</td><td></td></tr>"
                + "<tr><td>Contractor Cost</td><td>$</td><td>501,986</td></tr>"
                + "<tr><td>Total</td><td>$</td><td>8,500,000</td></tr>"
                + "</table></body></html>"
        );

        new CurrencyConsolidationStep().Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        rows[0]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent)
            .Should()
            .Equal("Use of Proceeds", "Initial Estimated Amount (US$)");
        rows[1]
            .QuerySelectorAll("td")
            .Select(cell => cell.TextContent)
            .Should()
            .Equal("Contractor Cost", "501,986");
        doc.QuerySelector("table + p em")!.TextContent.Should().Be("All values are in US Dollars.");
    }
}

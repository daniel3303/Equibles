using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientLiabilityDescriptionEmptyCreditorTests
{
    // Contract: a liability description reads "Type (Creditor)" only when both are present;
    // with one missing it uses the other alone. The sibling tests pin the creditor-alone leg
    // (Type column absent); this pins the symmetric type-alone leg — a present Type with a
    // blank Creditor cell must yield the type alone, never a "Type ()" trailing-parens wrapper.
    [Fact]
    public void ParseAnnualReportHtml_LiabilityWithEmptyCreditor_DescriptionIsTypeAlone()
    {
        const string html = """
            <html><body>
            <h3>Part 3. Assets</h3>
            <p>None disclosed.</p>
            <h3>Part 7. Liabilities</h3>
            <table>
              <thead><tr><th>Type</th><th>Amount</th><th>Creditor</th></tr></thead>
              <tbody><tr><td>Mortgage</td><td>$100,001 - $250,000</td><td></td></tr></tbody>
            </table>
            </body></html>
            """;

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines.Should().ContainSingle().Which.Description.Should().Be("Mortgage");
    }
}

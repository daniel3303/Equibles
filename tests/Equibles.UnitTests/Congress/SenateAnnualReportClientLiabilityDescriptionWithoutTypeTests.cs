using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientLiabilityDescriptionWithoutTypeTests
{
    // Contract: a liability line's description reads "Type (Creditor)" when both are present
    // (e.g. "Mortgage (Sample Bank)"). When the Type column is absent there is no type, so the
    // description must be the creditor alone — not an empty-type wrapper with a leading space.
    [Fact]
    public void ParseAnnualReportHtml_LiabilityWithoutTypeColumn_DescriptionIsCreditorAlone()
    {
        const string html = """
            <html><body>
            <h3>Part 3. Assets</h3>
            <p>None disclosed.</p>
            <h3>Part 7. Liabilities</h3>
            <table>
              <thead><tr><th>Debtor</th><th>Amount</th><th>Creditor</th></tr></thead>
              <tbody><tr><td>Self</td><td>$100,001 - $250,000</td><td>Sample Bank</td></tr></tbody>
            </table>
            </body></html>
            """;

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines.Should().ContainSingle().Which.Description.Should().Be("Sample Bank");
    }
}

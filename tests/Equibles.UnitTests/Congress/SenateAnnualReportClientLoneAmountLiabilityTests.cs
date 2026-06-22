using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientLoneAmountLiabilityTests
{
    // Contract: a liability line carries only the form's own brackets — "$X - $Y" or the
    // open-top "Over $X". A bare single amount with no range and no "Over" is "any other cell
    // content" and must yield no liability line.
    [Fact]
    public void ParseAnnualReportHtml_LiabilityAmountIsLoneValueWithoutRange_YieldsNoLiability()
    {
        const string html = """
            <html><body>
            <h3>Part 3. Assets</h3>
            <p>None disclosed.</p>
            <h3>Part 7. Liabilities</h3>
            <table>
              <thead><tr><th>Type</th><th>Amount</th><th>Creditor</th></tr></thead>
              <tbody><tr><td>Loan</td><td>$15,000</td><td>Sample Bank</td></tr></tbody>
            </table>
            </body></html>
            """;

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines.Should().NotContain(l => l.Kind == CongressionalDisclosureLineKind.Liability);
    }
}

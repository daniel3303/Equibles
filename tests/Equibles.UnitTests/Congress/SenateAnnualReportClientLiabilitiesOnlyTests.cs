using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientLiabilitiesOnlyTests
{
    // Contract: a senator can disclose no assets ("None disclosed." under
    // Part 3, so no assets table renders) while still reporting Part 7
    // liabilities — the electronic-layout gate keys on the Part 3 heading,
    // and the liability rows must come through.
    [Fact]
    public void ParseAnnualReportHtml_NoAssetsTableButLiabilities_KeepsTheLiabilityRows()
    {
        const string html = """
            <html><body>
            <h3>Part 3. Assets</h3>
            <p>None disclosed.</p>
            <h3>Part 7. Liabilities</h3>
            <table>
              <thead><tr><th></th><th>#</th><th>Incurred</th><th>Debtor</th><th>Type</th>
              <th>Points</th><th>Rate (Term)</th><th>Amount</th><th>Creditor</th><th>Comments</th></tr></thead>
              <tbody><tr><td></td><td>1</td><td>2011</td><td>Self</td><td>Mortgage</td>
              <td>0</td><td>4.875% (30 year fixed)</td><td>$100,001 - $250,000</td>
              <td>Sample Bank</td><td>n/a</td></tr></tbody>
            </table>
            </body></html>
            """;

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines.Should().NotBeNull("the Part 3 heading marks an electronic layout");
        var liability = lines.Should().ContainSingle().Subject;
        liability.Kind.Should().Be(CongressionalDisclosureLineKind.Liability);
        liability.Description.Should().Be("Mortgage (Sample Bank)");
        liability.RangeMinimum.Should().Be(100_001);
        liability.RangeMaximum.Should().Be(250_000);
    }
}

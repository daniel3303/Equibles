using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientLiabilityBothEmptyTests
{
    // Contract: a liability description is built from Type and Creditor, and the parser must
    // never emit a row with an empty description. The sibling tests pin each single-field leg
    // (creditor-alone, type-alone); this pins the combination both legs miss — a row whose Type
    // and Creditor cells are both blank, even with a valid amount range, yields no liability.
    [Fact]
    public void ParseAnnualReportHtml_LiabilityWithEmptyTypeAndCreditor_YieldsNoLiability()
    {
        const string html = """
            <html><body>
            <h3>Part 3. Assets</h3>
            <p>None disclosed.</p>
            <h3>Part 7. Liabilities</h3>
            <table>
              <thead><tr><th>Type</th><th>Amount</th><th>Creditor</th></tr></thead>
              <tbody><tr><td></td><td>$100,001 - $250,000</td><td></td></tr></tbody>
            </table>
            </body></html>
            """;

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines
            .Should()
            .NotContain(
                l => l.Kind == CongressionalDisclosureLineKind.Liability,
                "a liability row with no Type and no Creditor has no description and must be skipped"
            );
    }
}

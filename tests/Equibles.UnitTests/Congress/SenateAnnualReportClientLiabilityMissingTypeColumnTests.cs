using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientLiabilityMissingTypeColumnTests
{
    // Contract: ParseLiabilityTable is entered whenever the header row carries
    // both "creditor" and "amount" — "type" is NOT part of that gate. A Part 7
    // table that omits the Type column is therefore still a recognized
    // liability layout and must degrade gracefully (the electronic-layout
    // design never crashes on header variance), not throw while indexing a
    // missing column.
    [Fact]
    public void ParseAnnualReportHtml_LiabilityTableWithoutTypeColumn_DoesNotThrow()
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

        var act = () => SenateAnnualReportClient.ParseAnnualReportHtml(html);

        var lines = act.Should()
            .NotThrow(
                "a creditor+amount table without a Type column is a recognized liability layout"
            )
            .Subject;
        lines.Should().NotBeNull("the Part 3 heading marks an electronic layout");
        var liability = lines.Should().ContainSingle().Subject;
        liability.Kind.Should().Be(CongressionalDisclosureLineKind.Liability);
        liability.RangeMinimum.Should().Be(100_001);
        liability.RangeMaximum.Should().Be(250_000);
        liability.Description.Should().Contain("Sample Bank");
    }
}

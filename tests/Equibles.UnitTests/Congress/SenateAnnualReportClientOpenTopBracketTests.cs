using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientOpenTopBracketTests
{
    // Contract: eFD's top value bracket is open-ended ("Over $50,000,000").
    // It is a disclosed range, not a sentinel — the row must materialize with
    // both bounds at the bracket's floor rather than being dropped.
    [Fact]
    public void ParseAnnualReportHtml_OverFiftyMillionValue_ParsesAsOpenEndedBand()
    {
        const string html = """
            <html><body>
            <h3>Part 3. Assets</h3>
            <table>
              <thead><tr><th></th><th>Asset</th><th>Asset Type</th><th>Owner</th>
              <th>Value</th><th>Income Type</th><th>Income</th></tr></thead>
              <tbody><tr><td>1</td><td><strong>Family Holding LLC</strong></td>
              <td>Corporate Securities</td><td>Self</td><td>Over $50,000,000</td>
              <td>None</td><td>None</td></tr></tbody>
            </table>
            </body></html>
            """;

        var lines = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        lines.Should().NotBeNull();
        var asset = lines.Should().ContainSingle().Subject;
        asset.Kind.Should().Be(CongressionalDisclosureLineKind.Asset);
        asset.Description.Should().Be("Family Holding LLC");
        asset.RangeMinimum.Should().Be(50_000_000);
        asset.RangeMaximum.Should().Be(50_000_000);
    }
}

using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperDescriptionAssetColumnTests
{
    // Contract: MapColumnIndices ranks the asset column as asset+name, then a bare
    // "asset" (not "type"), then "description". When a table has no Asset/Asset Name
    // column, the parser must source the asset name from a "Description" column.
    // Without that third-rank fallback the asset name would come back empty even
    // though the disclosure clearly describes the security.
    [Fact]
    public void ParseTransactionsFromHtml_NoAssetColumnButDescriptionColumn_BindsAssetNameToDescription()
    {
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Description</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-03-15</td>
                  <td>AAPL</td>
                  <td>Apple Inc Common Stock</td>
                  <td>Purchase</td>
                  <td>$1,001 - $15,000</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        var result = DisclosureParsingHelper.ParseTransactionsFromHtml(
            html,
            "Test Member",
            CongressPosition.Representative,
            new DateOnly(2024, 4, 1),
            Substitute.For<ILogger>()
        );

        result.Should().ContainSingle();
        result[0].AssetName.Should().Be("Apple Inc Common Stock");
    }
}

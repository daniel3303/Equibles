using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperRowWithoutSecurityTests
{
    // Contract: a disclosure transaction must identify a security. ParseTransactionRow
    // drops any row that has a parseable date but neither a ticker nor an asset name —
    // such a row (a stray/blank/filler row in scraped HTML) carries no security and
    // must not become a transaction. A parser that emitted it would yield a record
    // with null ticker AND null asset, polluting downstream data.
    [Fact]
    public void ParseTransactionsFromHtml_RowWithDateButNoTickerOrAssetName_IsDropped()
    {
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-03-15</td>
                  <td></td>
                  <td></td>
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

        result.Should().BeEmpty();
    }
}

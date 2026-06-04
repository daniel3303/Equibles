using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperTheadlessHeaderTests
{
    // ExtractHeaderTexts reads `.//thead//th` OR falls back to `.//tr[1]//th`.
    // Senate disclosure tables sometimes put the header cells in a bare first <tr>
    // with no <thead> wrapper; the fallback is what lets those parse. Every other
    // ParseTransactionsFromHtml pin uses an explicit <thead>, so the fallback arm is
    // untested — dropping it would silently yield zero transactions for such tables.
    [Fact]
    public void ParseTransactionsFromHtml_HeadersInBareFirstRowWithoutThead_StillParsesTransaction()
    {
        var html = """
            <html><body>
            <table>
              <tr>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr>
              <tbody>
                <tr>
                  <td>2024-06-15</td>
                  <td>AAPL</td>
                  <td>Apple Inc</td>
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
            new DateOnly(2024, 7, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 6, 15));
    }
}

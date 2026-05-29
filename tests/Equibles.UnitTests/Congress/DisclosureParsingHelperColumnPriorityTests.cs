using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperColumnPriorityTests
{
    // Contract: when a disclosure table carries both a "Notification Date" and a
    // "Transaction Date" column, the parser must bind the date to the transaction
    // date — the predicate order in MapColumnIndices ranks "transaction"+"date"
    // ahead of "notification"+"date" and the generic "date" fallback. Notification
    // Date is placed first here so positional ("first header containing date")
    // selection would wrongly pick it.
    [Fact]
    public void ParseTransactionsFromHtml_BothNotificationAndTransactionDateColumns_SelectsTransactionDate()
    {
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Notification Date</th>
                <th>Transaction Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-01-15</td>
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

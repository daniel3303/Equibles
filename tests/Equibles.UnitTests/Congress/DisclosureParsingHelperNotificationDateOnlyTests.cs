using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class DisclosureParsingHelperNotificationDateOnlyTests
{
    // Contract: MapColumnIndices ranks the date column as transaction+date,
    // then notification+date, then any "date". The sibling priority test pins
    // the first rank. This pins the second: a table with ONLY a "Notification
    // Date" column (no transaction-date column) must still bind the transaction
    // date to that column — not drop the row for lack of a date. A parser that
    // recognised only "transaction date" would find no date and yield no rows.
    [Fact]
    public void ParseTransactionsFromHtml_OnlyNotificationDateColumn_BindsDateToNotificationColumn()
    {
        var html = """
            <html><body>
            <table>
              <thead><tr>
                <th>Notification Date</th>
                <th>Ticker</th>
                <th>Asset Name</th>
                <th>Transaction Type</th>
                <th>Amount</th>
              </tr></thead>
              <tbody>
                <tr>
                  <td>2024-03-15</td>
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
            new DateOnly(2024, 4, 1),
            Substitute.For<ILogger>()
        );

        result.Should().HaveCount(1);
        result[0].TransactionDate.Should().Be(new DateOnly(2024, 3, 15));
    }
}

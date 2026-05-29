using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientInvalidDateTests
{
    // Contract: the PTR anchor matches a date by digit-shape only (dd/dd/dddd),
    // so a row can be recognised as a transaction yet carry a non-calendar date
    // (OCR misreads of scanned House PTRs produce "13/45/2024" etc.).
    // BuildTransaction must drop such a row — never emit a transaction with a
    // bogus/default date. A parser that skipped date validation would persist a
    // garbage TransactionDate.
    [Fact]
    public void ParseTransactionLines_TransactionDateMatchesShapeButIsNotACalendarDate_DropsRow()
    {
        var result = HouseDisclosureClient.ParseTransactionLines(
            ["Apple Inc Common P 13/45/2024 $1,001 - $15,000"],
            "Nancy Pelosi",
            new DateOnly(2024, 7, 2)
        );

        result.Should().BeEmpty();
    }
}

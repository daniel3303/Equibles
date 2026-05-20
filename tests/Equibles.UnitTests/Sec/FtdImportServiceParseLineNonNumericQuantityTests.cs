using System.Reflection;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceParseLineNonNumericQuantityTests
{
    // Contract (newly extracted ParseLine helper, #1386): three explicit reject
    // paths — whitespace, fewer than 6 pipe-delimited fields, unparseable
    // SETTLEMENT DATE — all return null. Per the strict-null-on-malformed
    // precedent set by GH-1350 ("IsRecentFtdFile returns false for any
    // malformed input"), a non-numeric QUANTITY field is also malformed: a
    // careful caller of an FTD parser would not expect a fabricated
    // Quantity = 0 record (which silently corrupts downstream FTD aggregates
    // — zero failed-to-deliver shares is itself a meaningful and distinct
    // value from "we couldn't parse this row").
    [Fact(Skip = "GH-1438 — ParseLine emits record with Quantity=0 for non-numeric quantity")]
    public void ParseLine_NonNumericQuantity_ReturnsNull()
    {
        var method = typeof(FtdImportService).GetMethod(
            "ParseLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var line = "20240315|037833100|AAPL|notanumber|APPLE INC.|175.50";

        var result = (FtdRecord)method.Invoke(null, [line]);

        result.Should().BeNull();
    }
}

using System.Reflection;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceParseLineNonNumericPriceTests
{
    // Same strict-null-on-malformed precedent as GH-1350 (IsRecentFtdFile) and
    // GH-1438 (ParseLine non-numeric Quantity). PRICE is the sixth pipe-delimited
    // field; a non-numeric value cannot be a real FTD price. The parser must
    // reject the line, not fabricate Price = 0 — because Price = 0 is itself a
    // meaningful and observable value (delisted / no-bid securities), and
    // silently coercing "unparseable" into 0 corrupts downstream price-weighted
    // FTD aggregates with a value distinct from "we couldn't parse this row".
    [Fact(Skip = "GH-1596 — ParseLine emits record with Price=0 for non-numeric price")]
    public void ParseLine_NonNumericPrice_ReturnsNull()
    {
        var method = typeof(FtdImportService).GetMethod(
            "ParseLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var line = "20240315|037833100|AAPL|12345|APPLE INC.|notanumber";

        var result = (FtdRecord)method.Invoke(null, [line]);

        result.Should().BeNull();
    }
}

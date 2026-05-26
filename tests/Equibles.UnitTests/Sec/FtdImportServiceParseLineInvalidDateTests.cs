using System.Reflection;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceParseLineInvalidDateTests
{
    // Sibling to FtdImportServiceParseLineNonNumericQuantity / NonNumericPrice
    // pins. The reject ladder in ParseLine has three structurally distinct arms
    // (date / quantity / price). The date arm uses TryParseExact("yyyyMMdd"),
    // which is the strictest of the three — a refactor to plain TryParse would
    // silently accept "03/15/2024" or "2024-03-15", letting locale-dependent
    // dates pollute the FTD timeline (and SettlementDate is the primary key in
    // every downstream chart and CSV export). Pin the exact-format gate.
    [Fact]
    public void ParseLine_DateNotInYyyyMMddFormat_ReturnsNull()
    {
        var method = typeof(FtdImportService).GetMethod(
            "ParseLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var line = "2024-03-15|037833100|AAPL|123456|APPLE INC.|175.50";

        var result = (FtdRecord)method.Invoke(null, [line]);

        result.Should().BeNull();
    }
}

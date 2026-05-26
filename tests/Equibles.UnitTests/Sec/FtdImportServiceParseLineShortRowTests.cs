using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceParseLineShortRowTests
{
    // FtdImportService.ParseLine documents the field shape inline:
    //   "SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE"
    // The `parts.Length < 6` guard must return null on any row that doesn't
    // have all six pipes — SEC's FTD CSV downloads occasionally truncate
    // mid-line on transient gateway resets, and the parser is run row-by-row
    // across millions of records per filing. A refactor that "trusted the
    // shape" and indexed `parts[5]` directly would IndexOutOfRangeException
    // on the first short row and abort the entire FTD import for the day.
    [Fact]
    public void ParseLine_TooFewFields_ReturnsNullWithoutThrowing()
    {
        var method = typeof(FtdImportService).GetMethod(
            "ParseLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        object result = "not-null";
        var act = () => result = method.Invoke(null, ["20240930|000000000|AAPL"]);

        act.Should().NotThrow();
        result.Should().BeNull();
    }
}

using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientTryParseMasterIndexLineShortRowTests
{
    [Fact]
    public void TryParseMasterIndexLine_FewerThanFiveFields_ReturnsFalse()
    {
        // TryParseMasterIndexLine's early guard (SecEdgarClient.cs:506-507)
        // `if (fields.Length < 5) return false;` defends every subsequent
        // `fields[0]`..`fields[4]` access from IndexOutOfRangeException.
        // SEC's master.idx file routinely contains stub/preamble rows
        // ("Last Data Received: ...", divider lines, partial flush from
        // a truncated FTP stream) with fewer than five `|`-delimited fields.
        // Sibling pins cover the non-numeric CIK, non-13F formType, malformed
        // date, and lowercase formType arms — they all assume `fields[2]`,
        // `fields[3]`, `fields[4]` exist. A refactor that drops this length
        // guard (e.g. trusting "the daily file is always well-formed") would
        // compile, pass every existing pin, and crash the Realtime13F
        // discovery worker on the first partial line in any daily file.
        // Pin: line with only four pipe-delimited fields returns false, not
        // throws IndexOutOfRangeException on `fields[4]`.
        var method = typeof(SecEdgarClient).GetMethod(
            "TryParseMasterIndexLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var line = "0000320193|Apple Inc.|13F-HR|2024-11-01";
        object[] args = [line, new DateOnly(2024, 11, 1), null];

        var success = (bool)method!.Invoke(null, args);

        success.Should().BeFalse();
    }
}

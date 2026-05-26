using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceParseLineWhitespaceTests
{
    // Sibling to the short-row guard pin. SEC's FTD CSV interleaves data
    // rows with blank lines (the header section, separator before the
    // footer summary, and the trailing newline after the final row). The
    // leading `IsNullOrWhiteSpace` guard absorbs them so the parser's
    // body never indexes parts[0] on an empty split. A refactor that
    // dropped the guard would route every blank line into the `Split('|')`
    // path — yielding a 1-element array — and the next `parts.Length < 6`
    // check would catch the same input, but a future refactor that also
    // weakened the length guard would IOOR. Pin the early null-bail on
    // whitespace input to lock the first line of defense.
    [Fact]
    public void ParseLine_WhitespaceOnlyInput_ReturnsNullWithoutThrowing()
    {
        var method = typeof(FtdImportService).GetMethod(
            "ParseLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        object result = "not-null";
        var act = () => result = method.Invoke(null, ["   \t  "]);

        act.Should().NotThrow();
        result.Should().BeNull();
    }
}

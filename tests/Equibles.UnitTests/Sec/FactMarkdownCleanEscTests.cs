using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactMarkdownCleanEscTests
{
    [Fact]
    public void Clean_AnsiEscapeChar_IsStrippedAlongsideOtherControls()
    {
        // Sibling to CleanLogInjection. The existing pin proves CR/LF are
        // stripped (the most-obvious log-forging chars). ESC (`\x1B`, 0x1B)
        // is a separate threat class: terminal-rendered logs (this repo's
        // Serilog console sink + `docker logs`) interpret `\x1B[<n>m` as a
        // colour/cursor escape — a crafted MCP arg like `\x1B[2J\x1B[H`
        // clears the screen and homes the cursor, hiding subsequent log
        // lines from an operator triaging an incident. The contract is
        // `char.IsControl(c)` — which catches 0x1B — but a refactor that
        // narrows to `c is '\r' or '\n' or '\t'` (under the false intuition
        // that CR/LF/TAB enumerate the full intent) would compile, pass
        // CleanLogInjection, and leak ESC into the rendered logs. Pin the
        // ESC arm so that regression surfaces here.
        var result = FactMarkdown.Clean("safe\x1B[31mevil\x1B[0m");

        result.Should().Be("safe[31mevil[0m");
        result.Should().NotContain("\x1B");
    }
}

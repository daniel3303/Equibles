using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactMarkdownCleanC1NelTests
{
    // Third Clean control-char arm. The existing pins cover the C0 range
    // (CR/LF at 0x0D/0x0A, ESC at 0x1B - all below 0x20). NEL (NEXT LINE,
    // U+0085) lives in the C1 range (0x80-0x9F) and is a distinct threat
    // class:
    //
    //   - NEL is in Unicode category Cc so char.IsControl(c) returns true
    //     and the current implementation MUST strip it.
    //
    //   - A regression that narrowed Clean to "ASCII control chars only"
    //     (e.g. c less than space or c == 0x7F - a plausible misreading
    //     of the contract) would compile, pass every existing
    //     C0/CR/LF/ESC pin, and silently leak C1 chars into Serilog
    //     console output. NEL is interpreted as a line break by ECMA-48
    //     terminals - so a crafted MCP arg containing U+0085 could forge
    //     a second visible log entry, the same threat that
    //     CleanLogInjection pins against CR/LF.
    //
    // Contract (XML doc): "Strips control chars from untrusted args before
    // they reach logs". char.IsControl enumerates Unicode Cc, which
    // includes the full C1 block. Pin: assert NEL is stripped, defending
    // the FULL Cc contract rather than just the C0 portion already
    // covered. NEL is constructed via (char)0x85 to keep the source ASCII
    // and avoid editor/transport normalization of the literal codepoint.
    [Fact]
    public void Clean_NextLineC1Control_IsStrippedAlongsideC0Controls()
    {
        var nel = ((char)0x85).ToString();
        var input = "alert" + nel + "badhost";

        var result = FactMarkdown.Clean(input);

        result
            .Should()
            .Be(
                "alertbadhost",
                "U+0085 NEL is a Unicode Cc control char and must be stripped to defend the full char.IsControl contract"
            );
        result.Should().NotContain(nel);
    }
}

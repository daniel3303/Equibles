using System.Reflection;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class DocumentTextToolsHighlightKeywordNoMatchTests
{
    // Third pin in the HighlightKeyword family. Two siblings exist:
    //   • Empty-keyword DoS guard → return line unchanged (existing)
    //   • Keyword present with mixed casing → highlights all + preserves original casing
    //     (existing in DocumentTextToolsTests)
    // This pin covers the structurally distinct NO-MATCH path through the
    // while loop's `if (matchIndex < 0) { ... break; }` arm.
    //
    // The contract: when the keyword does not occur in the line, return the
    // line unchanged. SearchDocument's results return surrounding context
    // lines around each match — those context lines often contain no
    // keyword occurrence themselves, so the no-match path is the
    // production hot path for the function (every match has N context
    // lines around it, only 1 of which matches).
    //
    // The risk this pin uniquely catches and that the existing siblings
    // cannot:
    //   • Drop-the-`break` regression — `if (matchIndex < 0) { Append(...);
    //     /* missing break */ }` (a careless edit during cleanup) would
    //     loop forever because `index` is never advanced after a
    //     no-match. The empty-keyword sibling pin's WHY-comment explicitly
    //     mentions the analogous DoS, but that pin's input is ""; this
    //     pin uses a real-string keyword that simply doesn't occur.
    //   • Truncation regression — `result.Append(line, index, /* off by
    //     one */ line.Length - index - 1)` or `result.Append("")` (drop
    //     the remaining-line append entirely) would silently truncate or
    //     erase context lines. The casing sibling can't see this because
    //     its line CONTAINS the keyword — the loop hits the match path,
    //     not the no-match path.
    //   • Inversion regression — `if (matchIndex >= 0)` (logic flip) would
    //     route matching lines to the no-match handler and skip
    //     highlighting on every actual match. Caught by the casing pin.
    //     Conversely `if (matchIndex < 0) continue;` (no break, no
    //     append) — the loop body would not advance index, infinite loop.
    //     Caught here.
    //
    // Construction: a line that demonstrably does not contain the
    // keyword. Asserting that the returned string equals the original
    // line distinguishes the working no-match path from every truncation
    // and infinite-loop regression. The test must NOT hang (no [Fact]
    // timeout needed — VSTest's default-2-min cap catches the infinite-
    // loop case before it pollutes the whole suite).
    //
    // Reflection-invoke since HighlightKeyword is private static.
    [Fact]
    public void HighlightKeyword_KeywordDoesNotOccurInLine_ReturnsLineUnchanged()
    {
        var method = typeof(DocumentTextTools).GetMethod(
            "HighlightKeyword",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["hello world", "xyz"]);

        result.Should().Be("hello world");
    }
}

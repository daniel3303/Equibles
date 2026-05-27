using System.Reflection;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class DocumentTextToolsFormatLineTests
{
    // FormatLine is the per-line formatter every ReadDocumentLines /
    // SearchDocument MCP tool response flows through. The contract is the
    // interpolation:
    //   $"{lineNumber, 6} │ {content}"
    // — six-character right-aligned line number, the box-drawing vertical
    // bar (U+2502) wrapped in single spaces, then the line content.
    //
    // The MCP-tool LLM consumer keys on this exact shape to identify line
    // boundaries when iterating the returned text — a refactor that changed
    // any of the three structural elements (width, separator, padding)
    // would silently break the line-grouping behavior of every downstream
    // RAG response that parses the MCP output.
    //
    // The risk this pin uniquely catches:
    //   • Width refactor — `{lineNumber, 8}` ("just give numbers more
    //     room") would shift every content cell two columns right;
    //     downstream parsers that match on the `   42 │ ` width-6 pattern
    //     would fail to find the separator at the expected offset.
    //   • Separator refactor — replacing │ (U+2502 box-drawing vertical)
    //     with ASCII | ("nobody reads box drawing", "compatibility with
    //     7-bit terminals") would break the visual line in every MCP
    //     viewer and break any parser that matched the box character
    //     explicitly. Real ReadDocumentLines responses ship to LLMs that
    //     have been trained on the U+2502 shape (visible in the user-
    //     facing chat UI), so the visual regression is also a usability
    //     regression.
    //   • Order swap — `$"{content} │ {lineNumber, 6}"` (a careless
    //     argument re-order during cleanup) would print "abc │     42"
    //     instead of "    42 │ abc" — completely backwards.
    //   • Spacing drop — `$"{lineNumber, 6}│{content}"` (collapse the
    //     spaces) would visually run line number into separator into
    //     content with no gap.
    //
    // Pin: invoke with a small line number (asserting the width-6
    // padding) and a short content string. The EXACT expected output
    // is "    42 │ test" — four leading spaces + "42" + " │ " + "test".
    // Asserting on the exact string distinguishes all four regression
    // classes above. None of the existing FormatLine pins exist; this
    // is the first defense.
    //
    // Reflection-invoke since the helper is private static.
    [Fact]
    public void FormatLine_SmallLineNumber_RightAlignsToWidthSixWithBoxSeparator()
    {
        var method = typeof(DocumentTextTools).GetMethod(
            "FormatLine",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, [42, "test"]);

        result.Should().Be("    42 │ test");
    }
}

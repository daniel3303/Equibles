using System.Reflection;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class DocumentTextToolsHighlightKeywordCaseInsensitiveTests
{
    [Fact]
    public void HighlightKeyword_LowercaseKeywordMatchesUppercaseLine_WrapsOriginalCasing()
    {
        // HighlightKeyword's IndexOf uses StringComparison.OrdinalIgnoreCase
        // (DocumentTextTools.cs:181) so an LLM submitting a lowercase keyword
        // (the natural default — most LLM tool calls don't preserve casing)
        // still finds the uppercase or mixed-case occurrences in the source
        // SEC filing. A refactor that drops the StringComparison argument
        // would silently fall back to the default Ordinal (case-sensitive)
        // and the keyword highlighter would miss every case-mismatched hit.
        // The wrapping must preserve the line's ORIGINAL casing — the **
        // markers go around the SOURCE substring, not the search query.
        // Existing pins cover no-match and the empty-keyword DoS guard; this
        // closes the case-insensitivity contract.
        var method = typeof(DocumentTextTools).GetMethod(
            "HighlightKeyword",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["The COMPANY filed quarterly", "company"]);

        result.Should().Be("The **COMPANY** filed quarterly");
    }
}

using System.Reflection;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class DocumentTextToolsHighlightKeywordMultiMatchTests
{
    [Fact]
    public void HighlightKeyword_TwoMatchesInSameLine_BothWrappedAndInterveningTextPreserved()
    {
        // HighlightKeyword's loop advances `index = matchIndex + keyword.Length`
        // (DocumentTextTools.cs:192) so every occurrence in the line gets
        // wrapped, not just the first. A refactor that replaces the loop with
        // a single IndexOf + Substring stitch — easy to "simplify" to when the
        // intent looks like "wrap the keyword" — would compile, pass the
        // single-match and no-match pins, then silently wrap only the FIRST
        // occurrence on lines with repeated terms (an SEC filing where the
        // ticker AAPL appears multiple times in one risk-factor paragraph
        // would show only the first as bolded, misleading the LLM into
        // thinking the other occurrences are not the same entity).
        var method = typeof(DocumentTextTools).GetMethod(
            "HighlightKeyword",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["AAPL bought AAPL shares", "AAPL"]);

        result.Should().Be("**AAPL** bought **AAPL** shares");
    }
}

using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Cell's doc-comment says "Escapes the Markdown table delimiters
/// so a value containing '|' or a newline (e.g. some ADR/fund names) can't
/// break the table the LLM reads." Both classes of input — pipe and newline —
/// are the canonical attack vectors that corrupt a row's column count and
/// terminate the table early. A refactor that swaps backslash-escape for HTML
/// entity escape (which Markdown table parsers don't decode inside cells), or
/// forgets one of the two replacements, would compile cleanly and silently
/// produce a malformed table the LLM consumes as data.
/// </summary>
public class FactMarkdownCellEscapingTests
{
    [Fact]
    public void Cell_PipeAndNewline_ProduceMarkdownTableSafeOutput()
    {
        var result = FactMarkdown.Cell("a|b\nc");

        result.Should().Be("a\\|b c");
    }
}

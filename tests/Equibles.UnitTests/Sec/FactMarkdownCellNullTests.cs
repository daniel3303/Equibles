using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactMarkdownCellNullTests
{
    // Cell() runs on every Markdown table cell the FinancialFacts MCP tools
    // render, and the production callers feed it nullable strings — notably
    // `fact.Form?.DisplayName` (FinancialStatementTools.cs:187) where the
    // null-conditional propagates null when Form is absent. The leading
    // `value == null ? "" : …` short-circuit is the load-bearing safety —
    // a refactor that "simplified" Cell to `value.Replace(…)` (dropping
    // the null arm) would compile cleanly and NRE every time an MCP tool
    // rendered a row where any nullable field was actually null, aborting
    // the response mid-table for the LLM consumer.
    [Fact]
    public void Cell_NullInput_ReturnsEmptyStringWithoutThrowing()
    {
        string result = "not-empty";
        var act = () => result = FactMarkdown.Cell(null);

        act.Should().NotThrow();
        result.Should().Be(string.Empty);
    }
}

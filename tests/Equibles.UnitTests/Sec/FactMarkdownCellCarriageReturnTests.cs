using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Cell has three sanitization Replace calls — for '|', '\r' and
/// '\n'. The existing pin covers '|' and '\n' together; the '\r' arm is a
/// separate code path that fires when source data carries a bare CR (legacy
/// Mac line endings, half-stripped CRLF after some pipelines, or values
/// pasted from spreadsheets). A refactor that drops the '\r' Replace would
/// pass the existing pin and silently let CR through unsanitized — a bare CR
/// inside a Markdown table cell breaks the table the LLM reads downstream.
/// </summary>
public class FactMarkdownCellCarriageReturnTests
{
    [Fact]
    public void Cell_BareCarriageReturn_IsReplacedWithSpace()
    {
        var result = FactMarkdown.Cell("foo\rbar");

        result.Should().Be("foo bar");
    }
}

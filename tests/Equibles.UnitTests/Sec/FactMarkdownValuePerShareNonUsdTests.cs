using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactMarkdownValuePerShareNonUsdTests
{
    [Fact]
    public void Value_NonUsdPerSharesUnit_RendersCentsWithoutDollarPrefix()
    {
        // Sibling to ValuePerShareUsd. The contract's doc-comment is explicit:
        // "Only USD is prefixed with '$'; other currencies (EUR/GBP for 20-F /
        // 40-F filers) are conveyed by the unit column rather than mislabelled
        // with a dollar sign." A 20-F filer reporting EPS in EUR (e.g. ASML
        // at €5.23 per share) must render as "5.23" (cents preserved by the
        // per-share branch), NOT "$5.23" (would falsely imply USD to the LLM).
        // A refactor that drops the `isUsd ? "$" + number : number` final
        // ternary and unconditionally prefixes '$' would compile, pass the
        // USD pin, and silently relabel every foreign-filer EPS as dollars.
        var result = FactMarkdown.Value(5.23m, "EUR/shares");

        result.Should().Be("5.23");
    }
}

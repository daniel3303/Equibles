using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Value's doc-comment says: "Only USD is prefixed with '$';
/// other currencies (EUR/GBP for 20-F / 40-F filers) are conveyed by the
/// unit column rather than mislabelled with a dollar sign." A bare three-
/// letter currency code like "EUR" must therefore render as a grouped whole
/// number with no "$" prefix — the unit column carries the currency. A
/// refactor that generalises the USD branch into "is currency" (e.g. routing
/// IsBareCurrency through the isUsd predicate, or applying the prefix
/// whenever isWholeMagnitude is true) would compile cleanly and silently
/// mislabel every foreign-filer figure as dollars in the table the LLM reads.
/// </summary>
public class FactMarkdownValueBareCurrencyTests
{
    [Fact]
    public void Value_BareNonUsdCurrencyUnit_RendersGroupedWholeWithoutDollarPrefix()
    {
        var result = FactMarkdown.Value(1234567m, "EUR");

        result.Should().Be("1,234,567");
    }
}

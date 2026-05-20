using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Value's doc-comment guarantees that USD-denominated whole
/// monetary values render as a "$"-prefixed grouped whole number — this is
/// the dominant path for almost every fact in a 10-K (revenue, assets,
/// cash). Existing tests pin "USD/shares" (cents precision) and bare
/// non-USD codes (no prefix), but the plain "USD" path — where isPerShare
/// is false yet isUsd is true — is unpinned. A refactor that only treats
/// units starting with "USD/" as currency would drop the "$" and switch to
/// fractional precision for every base-currency figure.
/// </summary>
public class FactMarkdownValueBareUsdTests
{
    [Fact]
    public void Value_BareUsdUnit_RendersGroupedWholeWithDollarPrefix()
    {
        var result = FactMarkdown.Value(1234567m, "USD");

        result.Should().Be("$1,234,567");
    }
}

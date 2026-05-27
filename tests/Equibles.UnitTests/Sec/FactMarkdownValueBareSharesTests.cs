using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

public class FactMarkdownValueBareSharesTests
{
    [Fact]
    public void Value_BareSharesUnit_RendersGroupedWholeWithoutCurrencyPrefix()
    {
        // FactMarkdown.Value's `isWholeMagnitude` test is three-way:
        //   isUsd || string.Equals(u, "shares", OrdinalIgnoreCase) || IsBareCurrency(u)
        // Existing pins cover the USD and bare-currency (EUR) arms; "shares"
        // — the canonical SharesOutstanding/SharesIssued unit on every common-
        // stock fact — is the middle arm and unpinned. A refactor that
        // narrows the predicate to "is a currency" (dropping the equals-shares
        // disjunct) would silently route 1,000,000,000 shares through the
        // dimensionless "0.############" branch and render as "1000000000",
        // losing thousand separators that are the whole point of N0 for
        // human / LLM readability. Also asserts no "$" prefix — bare shares
        // are NOT a currency.
        var result = FactMarkdown.Value(1_234_567_890m, "shares");

        result.Should().Be("1,234,567,890");
    }
}

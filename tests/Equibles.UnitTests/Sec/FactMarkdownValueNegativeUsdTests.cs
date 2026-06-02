using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Value renders USD by concatenating "$" onto an already-signed
/// number string, so a negative figure puts the dollar sign between the sign
/// and the digits ("$-1,234,567"). Negative monetary facts are pervasive in
/// SEC filings (net loss, accumulated deficit, negative free cash flow) and the
/// XBRL parser already emits them from parenthesised values, so this malformed
/// "$-" notation reaches the LLM. The sign must precede the currency symbol,
/// matching the universal convention and .NET's own "C0" formatting.
/// </summary>
public class FactMarkdownValueNegativeUsdTests
{
    [Fact]
    public void Value_NegativeUsdUnit_RendersSignBeforeDollarPrefix()
    {
        var result = FactMarkdown.Value(-1234567m, "USD");

        result.Should().Be("-$1,234,567");
    }
}

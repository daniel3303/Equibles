using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Value(decimal, string) renders a USD monetary fact with a "$" prefix as a
/// grouped whole number. Negative monetary figures are pervasive in SEC facts
/// (net loss, accumulated deficit, negative free cash flow) and the XBRL
/// parser already emits them — parenthesised values like "(1,234)" parse to a
/// negative decimal that then flows straight into this renderer. Every
/// existing FactMarkdown.Value test uses a positive input, so the negative
/// path is unpinned. The contract promises standard currency rendering, and
/// the universal convention (matched by .NET's own currency formatting) places
/// the sign before the symbol: "-$1,234,567". Pin that so a negative net-loss
/// figure isn't handed to the LLM as malformed currency.
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

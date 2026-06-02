using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Value's contract layers three independent promises that a
/// negative USD/shares fact (e.g. basic loss per share) triggers at once:
/// per-share units render at cents precision, USD is "$"-prefixed, and the
/// sign precedes the currency symbol. Existing tests pin each axis in
/// isolation (positive USD/shares, positive bare USD, negative whole USD) but
/// never their intersection, so the negative-sign reattachment is unverified
/// on the cents-precision branch. Pin "-$1.50".
/// </summary>
public class FactMarkdownValueNegativePerShareUsdTests
{
    [Fact]
    public void Value_NegativePerShareUsdUnit_RendersSignBeforeDollarWithCents()
    {
        var result = FactMarkdown.Value(-1.5m, "USD/shares");

        result.Should().Be("-$1.50");
    }
}

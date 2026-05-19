using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Value(decimal, string) renders a reported fact for the MCP Markdown
/// surface. The doc-comment specifies: per-share units use cents precision,
/// USD gets the "$" prefix. The canonical SEC per-share-USD fact is earnings
/// per share — small magnitudes where the cents are the whole answer. If a
/// refactor checks isWholeMagnitude (USD → N0) before isPerShare ("/shares"
/// → N2), an EPS like $0.05 silently renders as "$0" and zeroes the figure
/// the LLM reads. Pin the combined branch so that regression surfaces here.
/// </summary>
public class FactMarkdownValuePerShareUsdTests
{
    [Fact]
    public void Value_UsdPerSharesUnit_RendersCentsWithDollarPrefix()
    {
        var result = FactMarkdown.Value(0.05m, "USD/shares");

        result.Should().Be("$0.05");
    }
}

using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FactMarkdown.Value's doc-comment promises that dimensionless / ratio units
/// (e.g. "pure") "keep their fractional precision rather than being rounded to
/// an integer." A regression that routed unknown / ratio units through the
/// grouped-whole-number "N0" arm would silently round every reported ratio
/// fact (P/E, margins, EPS-pure variants) to "0" in the MCP table output —
/// the exact failure mode the doc-comment names.
/// </summary>
public class FactMarkdownValuePureDimensionlessTests
{
    [Fact]
    public void Value_PureDimensionlessUnit_PreservesFractionalPrecision()
    {
        var result = FactMarkdown.Value(0.1234m, "pure");

        result.Should().Be("0.1234");
    }
}

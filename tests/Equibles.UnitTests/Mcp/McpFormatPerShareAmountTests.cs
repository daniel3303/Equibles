using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatPerShareAmountTests
{
    [Theory]
    [InlineData("0.2775", "0.2775")]
    [InlineData("0.525", "0.525")]
    [InlineData("0.28", "0.28")]
    [InlineData("1", "1.00")]
    [InlineData("0.48975694000000003", "0.489757")]
    public void PerShareAmount_KeepsTheDeclaredDecimals(string stored, string expected)
    {
        McpFormat
            .PerShareAmount(
                decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture)
            )
            .Should()
            .Be(expected);
    }
}

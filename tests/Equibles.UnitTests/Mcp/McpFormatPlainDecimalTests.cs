using System.Globalization;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatPlainDecimalTests
{
    [Theory]
    [InlineData("3.6300000000", "3.63")]
    [InlineData("24330.3745288000", "24330.3745288")]
    [InlineData("0.0000000000", "0")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("-0.0000045", "-0.0000045")]
    public void PlainDecimal_DropsTheStorageScaleWithoutScientificNotation(
        string stored,
        string expected
    )
    {
        McpFormat
            .Invariant(decimal.Parse(stored, CultureInfo.InvariantCulture), McpFormat.PlainDecimal)
            .Should()
            .Be(expected);
    }
}

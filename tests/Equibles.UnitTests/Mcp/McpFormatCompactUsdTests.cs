using System.Globalization;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatCompactUsdTests
{
    [Theory]
    [InlineData(1_230_000_000_000d, "$1.23T")]
    [InlineData(5_000_000_000d, "$5B")]
    [InlineData(456_000_000d, "$456M")]
    [InlineData(12_300d, "$12.3K")]
    [InlineData(999d, "$999")]
    [InlineData(0d, "$0")]
    public void CompactUsd_ScalesToTheLargestFittingSuffix(double value, string expected)
    {
        McpFormat.CompactUsd(value).Should().Be(expected);
    }

    [Fact]
    public void CompactUsd_Null_RendersDash()
    {
        McpFormat.CompactUsd(null).Should().Be("—");
    }

    [Fact]
    public void CompactUsd_IsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            McpFormat
                .CompactUsd(1_230_000_000d)
                .Should()
                .Be("$1.23B", "the decimal separator must not fork by host locale");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

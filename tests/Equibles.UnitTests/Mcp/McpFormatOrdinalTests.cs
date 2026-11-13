using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatOrdinalTests
{
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(51, "51st")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(113, "113th")]
    [InlineData(54, "54th")]
    [InlineData(0, "0th")]
    public void Ordinal_UsesTheEnglishSuffix(long value, string expected)
    {
        McpFormat.Ordinal(value).Should().Be(expected);
    }
}

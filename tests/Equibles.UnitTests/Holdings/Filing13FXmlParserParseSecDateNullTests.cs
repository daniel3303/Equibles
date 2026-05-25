using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserParseSecDateNullTests
{
    [Fact]
    public void ParseSecDate_NullInput_ReturnsMinValueNotThrow()
    {
        // Contract (doc-comment): "Returns DateOnly.MinValue when unparseable
        // so the downstream pipeline filters the filing out rather than crashing."
        // Null is the degenerate unparseable case.
        var parse = typeof(Filing13FXmlParser).GetMethod(
            "ParseSecDate",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (DateOnly)parse.Invoke(null, [null])!;

        result.Should().Be(DateOnly.MinValue);
    }
}

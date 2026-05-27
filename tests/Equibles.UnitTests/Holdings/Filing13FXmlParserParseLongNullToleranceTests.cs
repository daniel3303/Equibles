using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserParseLongNullToleranceTests
{
    // Filing13FXmlParser.ParseLong reads free-text numerics from the 13F
    // info-table XML (Shares, Value, voting-share triplets). The body uses
    // the null-conditional `raw?.Replace(",", "")` so callers can pass the
    // result of `Value(child)` (which returns null for an absent element)
    // without an upstream null-check — important because the info-table
    // schema makes most numeric fields optional and the parser is hit row-
    // by-row across the entire filing. A refactor that dropped the `?.` to
    // an unconditional `raw.Replace(...)` would compile cleanly and NRE on
    // the first absent element, aborting the import of the whole filing.
    [Fact]
    public void ParseLong_NullInput_ReturnsZeroWithoutThrowing()
    {
        var method = typeof(Filing13FXmlParser).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        long parsed = 0;
        var act = () => parsed = (long)method.Invoke(null, [null]);

        act.Should().NotThrow();
        parsed.Should().Be(0);
    }
}

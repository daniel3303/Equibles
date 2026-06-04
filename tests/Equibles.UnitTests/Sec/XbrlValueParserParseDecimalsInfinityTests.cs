using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class XbrlValueParserParseDecimalsInfinityTests
{
    // Per XBRL 2.1, @decimals="INF" means the fact is reported to infinite precision;
    // ParseDecimals maps that to int.MaxValue as the "round nowhere" sentinel. The
    // match is documented as case-insensitive, so a lowercase "inf" must resolve the
    // same way — a case-sensitive regression (== "INF") would return null and silently
    // demote an exact figure to "unknown precision".
    [Fact]
    public void ParseDecimals_LowercaseInf_ReturnsMaxValueSentinel()
    {
        var result = XbrlValueParser.ParseDecimals("inf");

        result.Should().Be(int.MaxValue);
    }
}

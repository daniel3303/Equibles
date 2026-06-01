using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class XbrlValueParserParseDecimalsNegativeTests
{
    // XBRL allows a negative @decimals (e.g. "-3" = reported accurate to the nearest
    // thousand). The sign carries meaning, so the parser must preserve it rather than
    // reject or absolutise it the way a "decimal places must be >= 0" assumption would.
    [Fact]
    public void ParseDecimals_NegativeIntegerDecimals_PreservesSign()
    {
        var result = XbrlValueParser.ParseDecimals("-3");

        result.Should().Be(-3);
    }
}

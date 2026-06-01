using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class XbrlValueParserStripPrefixEmptyLocalNameTests
{
    // A measure QName carrying a prefix but no local name (e.g. "iso4217:") is
    // unresolvable, so the contract promises null rather than an empty string.
    // Returning "" here would let a blank unit flow into a parsed fact instead
    // of the fact being dropped.
    [Fact]
    public void StripPrefix_PrefixWithEmptyLocalName_ReturnsNull()
    {
        var result = XbrlValueParser.StripPrefix("iso4217:");

        result.Should().BeNull();
    }
}

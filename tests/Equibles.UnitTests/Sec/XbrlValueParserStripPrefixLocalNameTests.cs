using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class XbrlValueParserStripPrefixLocalNameTests
{
    // The two primary StripPrefix paths (the empty-local-name → null edge is pinned
    // separately): a prefixed QName returns just the local name, and an unprefixed
    // name (no colon) passes through unchanged. Concept-tagged measures arrive both
    // ways — "us-gaap:Assets" from a namespaced taxonomy and a bare local name from a
    // default-namespace document — so both must resolve to the same concept token.
    [Fact]
    public void StripPrefix_PrefixedAndUnprefixedQNames_ReturnTheLocalName()
    {
        XbrlValueParser.StripPrefix("us-gaap:Assets").Should().Be("Assets");
        XbrlValueParser.StripPrefix("Assets").Should().Be("Assets");
    }
}

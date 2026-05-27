using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserDecimalsCaseInsensitiveInfTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:dei=\"http://xbrl.sec.gov/dei/2018-01-31\"";

    [Fact]
    public void Parse_DecimalsLowercaseInf_StillMapsToIntMaxValue()
    {
        // Sibling to the uppercase-INF pin. ParseDecimals
        // (StandaloneXbrlParser.cs:283-290) uses
        // `string.Equals(decimalsAttribute, "INF", OrdinalIgnoreCase)` so
        // mixed-case ("Inf") or lowercase ("inf") variants emitted by
        // off-spec filer toolchains still resolve to int.MaxValue (the
        // "infinite precision" signal SEC documents in the XBRL spec).
        // A refactor that swaps to `Ordinal` (single-keyword deletion)
        // would compile, pass the existing uppercase pin, and silently
        // route every lowercase-emitted INF through TryParse — which
        // returns false → null Decimals. Downstream rounding logic that
        // treats null Decimals as "round to the nearest whole" would
        // truncate share-count facts that were filed at exact precision.
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\"><xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period></xbrli:context>"
            + "<xbrli:unit id=\"shares\"><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unit>"
            + "<dei:EntityCommonStockSharesOutstanding "
            + "contextRef=\"C1\" unitRef=\"shares\" decimals=\"inf\">1000</dei:EntityCommonStockSharesOutstanding>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        fact.Decimals.Should().Be(int.MaxValue);
    }
}

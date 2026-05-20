using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class InlineXbrlParserDecimalsInfLowercaseTests
{
    // Sibling of Parse_DecimalsInf_MapsToIntMaxValue (which only pins "INF").
    // ParseDecimals uses OrdinalIgnoreCase to accept any case of "INF" — older
    // filings occasionally emit non-spec-strict lowercase. A refactor that
    // tightens the comparer to Ordinal (or replaces string.Equals with ==)
    // would silently route "inf" to int.TryParse, which fails, returning null
    // and losing the precision hint without the existing test catching it.
    [Fact]
    public void Parse_DecimalsLowercaseInf_MapsToIntMaxValue()
    {
        var html =
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" "
            + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
            + "xmlns:dei=\"http://xbrl.sec.gov/dei/2018-01-31\""
            + "><body><div style=\"display:none\"><ix:header><ix:resources>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unit>"
            + "</ix:resources></ix:header></div>"
            + "<ix:nonFraction name=\"dei:EntityCommonStockSharesOutstanding\" "
            + "contextRef=\"C1\" unitRef=\"u\" decimals=\"inf\">1000</ix:nonFraction>"
            + "</body></html>";

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Decimals.Should().Be(int.MaxValue);
    }
}

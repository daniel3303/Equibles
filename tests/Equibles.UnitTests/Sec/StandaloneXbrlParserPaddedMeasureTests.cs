using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Per the XBRL spec, <c>xbrli:measure</c> is typed as <c>xs:QName</c>, whose
/// whitespace facet is <c>collapse</c> — leading/trailing whitespace around the
/// QName must be discarded before resolving the measure. InlineXbrlParser
/// honours this by calling <c>.Trim()</c> on the measure text;
/// StandaloneXbrlParser does not, so a padded measure such as
/// <c>"  iso4217:USD  "</c> emits a fact with <c>Unit = "USD  "</c> (trailing
/// whitespace preserved) instead of <c>"USD"</c>. Every downstream
/// FinancialFacts tool keys off Unit ("USD", "shares/USD") to render the value
/// column, so the two parsers must agree.
/// </summary>
public class StandaloneXbrlParserPaddedMeasureTests
{
    [Fact]
    public void Parse_MeasureHasSurroundingWhitespace_TrimsBeforeResolving()
    {
        // Both parsers receive the same logical input; both must produce the
        // same Unit. The QName text is "  iso4217:USD  "; the resolved unit
        // (per xs:QName whitespace-collapse) must be "USD".
        var xml =
            "<xbrli:xbrl "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\">"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>  iso4217:USD  </xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">100</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var facts = new StandaloneXbrlParser().Parse(xml);

        facts.Should().ContainSingle().Which.Unit.Should().Be("USD");
    }
}

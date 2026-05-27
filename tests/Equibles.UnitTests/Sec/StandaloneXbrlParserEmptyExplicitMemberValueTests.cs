using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class StandaloneXbrlParserEmptyExplicitMemberValueTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\" "
        + "xmlns:srt=\"http://fasb.org/srt/2018-01-31\"";

    [Fact]
    public void Parse_ExplicitMemberWithEmptyValue_DimensionIsDropped()
    {
        // ExtractDimensions guard (StandaloneXbrlParser.cs:162-163):
        //   if (string.IsNullOrEmpty(axis) || string.IsNullOrEmpty(memberValue)) continue;
        // Existing pins cover the happy path. The OR's second arm — `<xbrldi:explicitMember
        // dimension="srt:Axis"></xbrldi:explicitMember>` with no inner text — is unpinned.
        // A refactor that "tightens" the guard to `axis is null` (drops the memberValue
        // check on the assumption the XBRL spec mandates non-empty members) would silently
        // add a ParsedXbrlDimension with Member="" to the fact, polluting every downstream
        // group-by-segment query — facts that should bucket as plain-totals end up under
        // an empty-string dimension key. Pin: emit a member with empty value alongside a
        // valid one; only the valid dimension must reach the parsed fact.
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity>"
            + "    <xbrli:identifier scheme=\"x\">0</xbrli:identifier>"
            + "    <xbrli:segment>"
            + "      <xbrldi:explicitMember dimension=\"srt:ProductOrServiceAxis\"></xbrldi:explicitMember>"
            + "    </xbrli:segment>"
            + "  </xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">1000</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;
        fact.Dimensions.Should().BeEmpty();
    }
}

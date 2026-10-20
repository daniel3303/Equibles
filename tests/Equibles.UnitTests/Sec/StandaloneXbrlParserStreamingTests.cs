using System.Text;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the two properties the streaming rewrite of StandaloneXbrlParser
/// depends on, neither of which the previous LINQ-to-XML implementation had to
/// think about because the whole document was in memory before any work began.
///
/// <para>
/// 1. <b>Declaration order must not matter.</b> The parser now reads contexts
/// and units in a first pass and facts in a second, so a fact that appears
/// before the context it references still resolves. A single-pass reader that
/// resolved refs on the fly would silently drop that fact - which is exactly the
/// kind of data loss this parser must never introduce, and which no existing
/// test covers.
/// </para>
/// <para>
/// 2. <b>Fact count must not depend on size.</b> The change exists to make peak
/// memory independent of filing size, so a document with thousands of facts has
/// to come back complete. This is not a memory assertion (that would be flaky);
/// it is a wholesale-drop assertion.
/// </para>
/// </summary>
public class StandaloneXbrlParserStreamingTests
{
    private const string XbrliNs = "http://www.xbrl.org/2003/instance";
    private const string UsGaapNs = "http://fasb.org/us-gaap/2023";

    [Fact]
    public void Parse_FactDeclaredBeforeItsContext_StillResolves()
    {
        // The us-gaap:Revenues element precedes both the context and the unit it
        // references. A one-pass resolver would drop it; the two-pass reader must
        // not.
        var xml =
            $"<xbrli:xbrl xmlns:xbrli=\"{XbrliNs}\" xmlns:us-gaap=\"{UsGaapNs}\">"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\" decimals=\"-6\">1234</us-gaap:Revenues>"
            + "<xbrli:context id=\"C1\">"
            + "<xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">320193</xbrli:identifier></xbrli:entity>"
            + "<xbrli:period><xbrli:instant>2023-09-30</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "</xbrli:xbrl>";

        var facts = new StandaloneXbrlParser().Parse(xml);

        facts.Should().ContainSingle();
        facts[0].Tag.Should().Be("Revenues");
        facts[0].Taxonomy.Should().Be("us-gaap");
        facts[0].Value.Should().Be(1234m);
        facts[0].IsInstant.Should().BeTrue();
        facts[0].Decimals.Should().Be(-6);
        facts[0].ConsolidatedCik.Should().Be("320193");
    }

    [Fact]
    public void Parse_ThousandsOfFacts_ReturnsEveryOne()
    {
        const int count = 2000;
        var builder = new StringBuilder();
        builder.Append($"<xbrli:xbrl xmlns:xbrli=\"{XbrliNs}\" xmlns:us-gaap=\"{UsGaapNs}\">");
        builder.Append("<xbrli:context id=\"C1\">");
        builder.Append(
            "<xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">1</xbrli:identifier></xbrli:entity>"
        );
        builder.Append(
            "<xbrli:period><xbrli:startDate>2023-01-01</xbrli:startDate><xbrli:endDate>2023-12-31</xbrli:endDate></xbrli:period>"
        );
        builder.Append("</xbrli:context>");
        builder.Append(
            "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
        );
        for (var i = 0; i < count; i++)
            builder.Append(
                $"<us-gaap:Line{i} contextRef=\"C1\" unitRef=\"u\">{i}</us-gaap:Line{i}>"
            );
        builder.Append("</xbrli:xbrl>");

        var facts = new StandaloneXbrlParser().Parse(builder.ToString());

        facts.Should().HaveCount(count);
        // Not just the count: the first and last must carry their own values, so a
        // reader that mis-advances after ReadElementContentAsString cannot pass by
        // emitting the same fact twice.
        facts[0].Value.Should().Be(0m);
        facts[count - 1].Value.Should().Be(count - 1);
        facts[count - 1].Tag.Should().Be($"Line{count - 1}");
        facts.Should().OnlyContain(f => !f.IsInstant);
    }
}

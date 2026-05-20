using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the standalone XBRL parser's extraction contract for #877. Fixtures
/// are inlined per test so the asserted XBRL shape stays adjacent to the
/// behaviour it pins (and so the test file is self-contained).
/// </summary>
public class StandaloneXbrlParserTests
{
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\" "
        + "xmlns:srt=\"http://fasb.org/srt/2018-01-31\" "
        + "xmlns:aapl=\"http://www.apple.com/20180929\"";

    [Fact]
    public void Parse_NumericFactWithInstantContext_ResolvesContextAndUnit()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">0000320193</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2018-09-29</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"usd\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Assets contextRef=\"C1\" unitRef=\"usd\" decimals=\"-6\">365725000000</us-gaap:Assets>"
            + "</xbrli:xbrl>";

        var facts = new StandaloneXbrlParser().Parse(xml);

        var fact = facts.Should().ContainSingle().Subject;
        fact.Taxonomy.Should().Be("us-gaap");
        fact.Tag.Should().Be("Assets");
        fact.Unit.Should().Be("USD");
        fact.Value.Should().Be(365_725_000_000m);
        fact.IsInstant.Should().BeTrue();
        fact.PeriodStart.Should().Be(new DateOnly(2018, 9, 29));
        fact.PeriodEnd.Should().Be(new DateOnly(2018, 9, 29));
        fact.Decimals.Should().Be(-6);
        fact.Dimensions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DurationPeriod_PreservesStartAndEnd()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"D1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:startDate>2017-10-01</xbrli:startDate><xbrli:endDate>2018-09-29</xbrli:endDate></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"D1\" unitRef=\"u\">265595000000</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        fact.IsInstant.Should().BeFalse();
        fact.PeriodStart.Should().Be(new DateOnly(2017, 10, 1));
        fact.PeriodEnd.Should().Be(new DateOnly(2018, 9, 29));
    }

    [Fact]
    public void Parse_ExplicitMemberInSegment_ExtractsDimension()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity>"
            + "    <xbrli:identifier scheme=\"x\">0</xbrli:identifier>"
            + "    <xbrli:segment>"
            + "      <xbrldi:explicitMember dimension=\"srt:ProductOrServiceAxis\">aapl:IPhoneMember</xbrldi:explicitMember>"
            + "    </xbrli:segment>"
            + "  </xbrli:entity>"
            + "  <xbrli:period><xbrli:startDate>2017-10-01</xbrli:startDate><xbrli:endDate>2018-09-29</xbrli:endDate></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">164888000000</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        var dimension = fact.Dimensions.Should().ContainSingle().Subject;
        dimension.Axis.Should().Be("srt:ProductOrServiceAxis");
        dimension.Member.Should().Be("aapl:IPhoneMember");
    }

    [Fact]
    public void Parse_MultipleExplicitMembers_AllExtracted()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity>"
            + "    <xbrli:identifier scheme=\"x\">0</xbrli:identifier>"
            + "    <xbrli:segment>"
            + "      <xbrldi:explicitMember dimension=\"srt:ProductOrServiceAxis\">aapl:IPhoneMember</xbrldi:explicitMember>"
            + "      <xbrldi:explicitMember dimension=\"srt:StatementGeographicalAxis\">aapl:AmericasMember</xbrldi:explicitMember>"
            + "    </xbrli:segment>"
            + "  </xbrli:entity>"
            + "  <xbrli:period><xbrli:startDate>2017-10-01</xbrli:startDate><xbrli:endDate>2018-09-29</xbrli:endDate></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\">1000</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        fact.Dimensions.Should()
            .HaveCount(2)
            .And.Contain(d =>
                d.Axis == "srt:ProductOrServiceAxis" && d.Member == "aapl:IPhoneMember"
            )
            .And.Contain(d =>
                d.Axis == "srt:StatementGeographicalAxis" && d.Member == "aapl:AmericasMember"
            );
    }

    [Fact]
    public void Parse_DivideUnit_ResolvesAsNumeratorOverDenominator()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2018-09-29</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"usdPerShare\">"
            + "  <xbrli:divide>"
            + "    <xbrli:unitNumerator><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unitNumerator>"
            + "    <xbrli:unitDenominator><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unitDenominator>"
            + "  </xbrli:divide>"
            + "</xbrli:unit>"
            + "<us-gaap:EarningsPerShareBasic contextRef=\"C1\" unitRef=\"usdPerShare\" decimals=\"2\">12.01</us-gaap:EarningsPerShareBasic>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        fact.Unit.Should().Be("USD/shares");
        fact.Value.Should().Be(12.01m);
    }

    [Fact]
    public void Parse_DecimalsInf_MapsToIntMaxValue()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\"><xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period></xbrli:context>"
            + "<xbrli:unit id=\"shares\"><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unit>"
            + "<dei:EntityCommonStockSharesOutstanding xmlns:dei=\"http://xbrl.sec.gov/dei/2018-01-31\" "
            + "contextRef=\"C1\" unitRef=\"shares\" decimals=\"INF\">1000</dei:EntityCommonStockSharesOutstanding>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        fact.Decimals.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Parse_ContextRefMissing_SkipsFact()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"DoesNotExist\" unitRef=\"u\">1</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }

    [Fact]
    public void Parse_UnitRefMissing_SkipsFact()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\"><xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period></xbrli:context>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"missing\">1</us-gaap:Revenues>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }

    [Fact]
    public void Parse_XsiNilTrue_SkipsFact()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\"><xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period></xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<us-gaap:Revenues contextRef=\"C1\" unitRef=\"u\" xsi:nil=\"true\"/>"
            + "</xbrli:xbrl>";

        new StandaloneXbrlParser().Parse(xml).Should().BeEmpty();
    }

    [Fact]
    public void Parse_FilerExtensionTaxonomy_PreservesPrefix()
    {
        var xml =
            $"<xbrli:xbrl {Namespaces}>"
            + "<xbrli:context id=\"C1\"><xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period></xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + "<aapl:CustomMetric contextRef=\"C1\" unitRef=\"u\">42</aapl:CustomMetric>"
            + "</xbrli:xbrl>";

        var fact = new StandaloneXbrlParser().Parse(xml).Should().ContainSingle().Subject;

        fact.Taxonomy.Should().Be("aapl");
        fact.Tag.Should().Be("CustomMetric");
    }

    [Fact]
    public void Parse_InvalidXml_ReturnsEmpty()
    {
        new StandaloneXbrlParser().Parse("<not-xbrl><broken</not-xbrl>").Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullOrWhitespace_ReturnsEmpty()
    {
        var parser = new StandaloneXbrlParser();
        parser.Parse(null).Should().BeEmpty();
        parser.Parse("").Should().BeEmpty();
        parser.Parse("   ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_WrongRoot_ReturnsEmpty()
    {
        new StandaloneXbrlParser().Parse("<not-the-root xmlns=\"x\"/>").Should().BeEmpty();
    }
}

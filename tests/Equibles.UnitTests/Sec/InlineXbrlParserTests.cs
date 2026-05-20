using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the inline iXBRL parser's extraction contract for #877. Fixtures
/// are inlined per test so the asserted document shape stays adjacent to
/// the behaviour it pins. The wrapper always carries the namespaces every
/// iXBRL fixture needs (xbrli, xbrldi, ix, ixt, and the taxonomies under
/// test) so each test only states the iXBRL fragment that matters.
/// </summary>
public class InlineXbrlParserTests
{
    private const string DocOpen =
        "<html "
        + "xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2015-02-26\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2018-01-31\" "
        + "xmlns:dei=\"http://xbrl.sec.gov/dei/2018-01-31\" "
        + "xmlns:srt=\"http://fasb.org/srt/2018-01-31\" "
        + "xmlns:aapl=\"http://www.apple.com/20180929\""
        + "><body><div style=\"display:none\"><ix:header><ix:resources>";

    private const string ResourcesEnd = "</ix:resources></ix:header></div>";

    private const string DocClose = "</body></html>";

    [Fact]
    public void Parse_NonFractionInstantContext_ExtractsConceptAndValue()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2018-09-29</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"usd\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<p>Total: <ix:nonFraction name=\"us-gaap:Assets\" contextRef=\"C1\" unitRef=\"usd\" decimals=\"-6\">365725000000</ix:nonFraction></p>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Taxonomy.Should().Be("us-gaap");
        fact.Tag.Should().Be("Assets");
        fact.Unit.Should().Be("USD");
        fact.Value.Should().Be(365_725_000_000m);
        fact.IsInstant.Should().BeTrue();
        fact.PeriodStart.Should().Be(new DateOnly(2018, 9, 29));
        fact.Decimals.Should().Be(-6);
        fact.Dimensions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ScaleAttribute_MultipliesValue()
    {
        // scale="6" + raw "265,595" → 265,595 × 10^6 = 265,595,000,000.
        // Common pattern for "$ in millions" tables.
        var html =
            DocOpen
            + "<xbrli:context id=\"D1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:startDate>2017-10-01</xbrli:startDate><xbrli:endDate>2018-09-29</xbrli:endDate></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"D1\" unitRef=\"u\" decimals=\"-6\" scale=\"6\" format=\"ixt:numdotdecimal\">265,595</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(265_595_000_000m);
        fact.IsInstant.Should().BeFalse();
        fact.PeriodEnd.Should().Be(new DateOnly(2018, 9, 29));
    }

    [Fact]
    public void Parse_ParenthesisedValue_IsNegated()
    {
        // "(1,234)" is the accounting-format negative; parser must flip
        // the sign even without an explicit sign="-" attribute.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:NetIncomeLoss\" contextRef=\"C1\" unitRef=\"u\">(1,234)</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(-1234m);
    }

    [Fact]
    public void Parse_SignAttributeNegative_NegatesValue()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:NetIncomeLoss\" contextRef=\"C1\" unitRef=\"u\" sign=\"-\">500</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(-500m);
    }

    [Fact]
    public void Parse_NumDashFormat_ReturnsZero()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\" format=\"ixt:numdash\">&#8212;</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(0m);
    }

    [Fact]
    public void Parse_ExplicitMemberInSegment_ExtractsDimension()
    {
        var html =
            DocOpen
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
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">164888</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        var dimension = fact.Dimensions.Should().ContainSingle().Subject;
        dimension.Axis.Should().Be("srt:ProductOrServiceAxis");
        dimension.Member.Should().Be("aapl:IPhoneMember");
    }

    [Fact]
    public void Parse_DivideUnit_ResolvesAsNumeratorOverDenominator()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"usdPerShare\">"
            + "  <xbrli:divide>"
            + "    <xbrli:unitNumerator><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unitNumerator>"
            + "    <xbrli:unitDenominator><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unitDenominator>"
            + "  </xbrli:divide>"
            + "</xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:EarningsPerShareBasic\" contextRef=\"C1\" unitRef=\"usdPerShare\" decimals=\"2\">12.01</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Unit.Should().Be("USD/shares");
        fact.Value.Should().Be(12.01m);
    }

    [Fact]
    public void Parse_DecimalsInf_MapsToIntMaxValue()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"dei:EntityCommonStockSharesOutstanding\" contextRef=\"C1\" unitRef=\"u\" decimals=\"INF\">1000</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Decimals.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Parse_ContextRefMissing_SkipsFact()
    {
        var html =
            DocOpen
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"missing\" unitRef=\"u\">1</ix:nonFraction>"
            + DocClose;

        new InlineXbrlParser().Parse(html).Should().BeEmpty();
    }

    [Fact]
    public void Parse_NameAttributeMissing_SkipsFact()
    {
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction contextRef=\"C1\" unitRef=\"u\">1</ix:nonFraction>"
            + DocClose;

        new InlineXbrlParser().Parse(html).Should().BeEmpty();
    }

    [Fact]
    public void Parse_NumCommaDecimalFormat_HandlesEuropeanDecimal()
    {
        // European format: "1.234,56" means 1234.56 — dot is thousands,
        // comma is decimal. Parser must NOT treat the dot as decimal.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"ifrs-full:Revenue\" contextRef=\"C1\" unitRef=\"u\" format=\"ixt:numcommadecimal\">1.234,56</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(1234.56m);
        fact.Taxonomy.Should().Be("ifrs-full");
    }

    [Fact]
    public void Parse_CurrencyAndNbspGlyphs_AreStripped()
    {
        // U+00A0 NBSP between digit groups and an explicit '$' prefix are
        // common formatting filers paste in. Both must drop out before
        // decimal parsing.
        var html =
            DocOpen
            + "<xbrli:context id=\"C1\">"
            + "  <xbrli:entity><xbrli:identifier scheme=\"x\">0</xbrli:identifier></xbrli:entity>"
            + "  <xbrli:period><xbrli:instant>2020-01-01</xbrli:instant></xbrli:period>"
            + "</xbrli:context>"
            + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
            + ResourcesEnd
            + "<ix:nonFraction name=\"us-gaap:Revenues\" contextRef=\"C1\" unitRef=\"u\">$ 1 234</ix:nonFraction>"
            + DocClose;

        var fact = new InlineXbrlParser().Parse(html).Should().ContainSingle().Subject;

        fact.Value.Should().Be(1234m);
    }

    [Fact]
    public void Parse_EmptyOrNullInput_ReturnsEmpty()
    {
        var parser = new InlineXbrlParser();
        parser.Parse(null).Should().BeEmpty();
        parser.Parse("").Should().BeEmpty();
        parser.Parse("   ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoFactsInDocument_ReturnsEmpty()
    {
        new InlineXbrlParser()
            .Parse("<html><body><p>Just regular HTML</p></body></html>")
            .Should()
            .BeEmpty();
    }
}

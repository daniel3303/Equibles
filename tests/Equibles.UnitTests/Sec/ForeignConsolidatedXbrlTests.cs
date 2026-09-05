using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class ForeignConsolidatedXbrlTests
{
    [Theory]
    [InlineData("SixK", "1089113", true)]
    [InlineData("SixKa", "0001089113", true)]
    [InlineData("TwentyF", "0001089113", true)]
    [InlineData("TwentyFa", "0001089113", true)]
    [InlineData("FortyF", "0001089113", true)]
    [InlineData("FortyFa", "0001089113", true)]
    [InlineData("TenK", "0001089113", false)]
    [InlineData("EightK", "0001089113", false)]
    [InlineData("SixK", "9999999", false)]
    [InlineData("SixK", "", false)]
    public void Select_RequiresForeignReportAndExactIssuer(string form, string cik, bool admitted)
    {
        var parsed = new InlineXbrlParser().Parse(Inline());
        var selected = XbrlFactExtractionService.SelectPersistable(
            parsed,
            new Document
            {
                DocumentType = DocumentType.FromValue(form),
                CommonStock = new CommonStock { Cik = cik },
            }
        );

        selected.Count.Should().Be(admitted ? 1 : 0);
        parsed.Single().Value.Should().Be(37_742_000_000m);
        parsed.Single().PeriodStart.Should().Be(new DateOnly(2026, 1, 1));
        parsed.Single().PeriodEnd.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Theory]
    [InlineData("<xbrli:scenario><other>parent only</other></xbrli:scenario>")]
    [InlineData(
        "<xbrli:segment><xbrldi:typedMember dimension=\"issuer:CustomerAxis\"><customer>A</customer></xbrldi:typedMember></xbrli:segment>"
    )]
    public void Parse_QualifiedContextNeverProvesConsolidatedCik(string qualifier)
    {
        new InlineXbrlParser().Parse(Inline(qualifier)).Single().ConsolidatedCik.Should().BeNull();
        new StandaloneXbrlParser()
            .Parse(Standalone(qualifier))
            .Single()
            .ConsolidatedCik.Should()
            .BeNull();
    }

    [Fact]
    public void Parse_StandalonePreservesIssuerAndSourceValue()
    {
        var fact = new StandaloneXbrlParser().Parse(Standalone()).Single();
        fact.ConsolidatedCik.Should().Be("0001089113");
        fact.Value.Should().Be(37_742_000_000m);
    }

    [Fact]
    public void Parse_UnknownIdentifierSchemeDoesNotProveIssuer()
    {
        new InlineXbrlParser()
            .Parse(Inline().Replace("http://www.sec.gov/CIK", "other"))
            .Single()
            .ConsolidatedCik.Should()
            .BeNull();
    }

    [Fact]
    public void Parse_RepeatedContextIdDoesNotProveConsolidatedIdentity()
    {
        new InlineXbrlParser()
            .Parse(Inline().Replace(Context(), Context() + Context()))
            .Single()
            .ConsolidatedCik.Should()
            .BeNull();
    }

    // Reduced filing shape: a consolidated IFRS half-year row with inline scale,
    // separate context and ISO currency unit; no dependence on network or current data.
    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" xmlns:ifrs-full=\"https://xbrl.ifrs.org/taxonomy/2025-03-27/ifrs-full\" xmlns:iso4217=\"http://www.xbrl.org/2003/iso4217\"";
    private const string Unit =
        "<xbrli:unit id=\"usd\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>";

    private static string Context(string qualifier = "") =>
        $"<xbrli:context id=\"c1\"><xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">0001089113</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2026-01-01</xbrli:startDate><xbrli:endDate>2026-06-30</xbrli:endDate></xbrli:period>{qualifier}</xbrli:context>";

    private static string Inline(string qualifier = "") =>
        $"<html {Namespaces} xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body>{Context(qualifier)}{Unit}<ix:nonFraction name=\"ifrs-full:RevenueAndOperatingIncome\" contextRef=\"c1\" unitRef=\"usd\" scale=\"6\" decimals=\"-6\">37,742</ix:nonFraction></body></html>";

    private static string Standalone(string qualifier = "") =>
        $"<xbrli:xbrl {Namespaces}>{Context(qualifier)}{Unit}<ifrs-full:RevenueAndOperatingIncome contextRef=\"c1\" unitRef=\"usd\" decimals=\"-6\">37742000000</ifrs-full:RevenueAndOperatingIncome></xbrli:xbrl>";
}

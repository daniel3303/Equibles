using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

// A European annual report states its filer under ISO 17442 rather than as a CIK. The shapes mirror
// ForeignConsolidatedXbrlTests exactly, so the two identity arms are pinned the same way.
public class EsefConsolidatedXbrlTests
{
    private const string Lei = "529900S21EQ1BO4ESM68";
    private const string LeiScheme = "http://standards.iso.org/iso/17442";

    [Theory]
    [InlineData("EsefAnnualReport", Lei, true)]
    [InlineData("EsefAnnualReport", "529900S21EQ1BO4ESM60", false)]
    [InlineData("EsefAnnualReport", "", false)]
    // The report form decides which identity arm runs, so an SEC form never admits an LEI context.
    [InlineData("TwentyF", Lei, false)]
    [InlineData("SixK", Lei, false)]
    [InlineData("TenK", Lei, false)]
    public void Select_RequiresTheEsefFormAndTheExactIssuerLei(
        string form,
        string issuerLei,
        bool admitted
    )
    {
        var parsed = new InlineXbrlParser().Parse(Inline());
        var selected = XbrlFactExtractionService.SelectPersistable(
            parsed,
            new Document
            {
                DocumentType = DocumentType.FromValue(form),
                Issuer = new EquityIssuer { LegalEntityIdentifier = issuerLei },
            }
        );

        selected.Count.Should().Be(admitted ? 1 : 0);
        parsed.Single().Value.Should().Be(37_742_000_000m);
    }

    // The issuer identity is one fixed-width code with no leading-zero convention, so the comparison is
    // exact apart from case; it must never borrow the CIK arm's zero trimming.
    [Fact]
    public void Select_ComparesTheIdentifierCaseInsensitivelyAndNotByTrimming()
    {
        var parsed = new InlineXbrlParser().Parse(Inline().Replace(Lei, Lei.ToLowerInvariant()));

        XbrlFactExtractionService
            .SelectPersistable(
                parsed,
                new Document
                {
                    DocumentType = DocumentType.EsefAnnualReport,
                    Issuer = new EquityIssuer { LegalEntityIdentifier = Lei },
                }
            )
            .Should()
            .HaveCount(1);
    }

    // An ESEF document whose issuer carries only a CIK proves nothing: the two arms never cross.
    [Fact]
    public void Select_DoesNotFallBackToTheCikArmForAnEsefReport()
    {
        XbrlFactExtractionService
            .SelectPersistable(
                new InlineXbrlParser().Parse(Inline()),
                new Document
                {
                    DocumentType = DocumentType.EsefAnnualReport,
                    Issuer = new EquityIssuer { Cik = "0001089113" },
                }
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Parse_BothParsersReadTheIso17442Identity()
    {
        new InlineXbrlParser().Parse(Inline()).Single().ConsolidatedLei.Should().Be(Lei);
        new StandaloneXbrlParser().Parse(Standalone()).Single().ConsolidatedLei.Should().Be(Lei);
    }

    // A European report carries no CIK, and reading one would be the cross-scheme confusion this arm exists
    // to prevent.
    [Fact]
    public void Parse_AnIso17442IdentityIsNeverReadAsACik()
    {
        new InlineXbrlParser().Parse(Inline()).Single().ConsolidatedCik.Should().BeNull();
        new StandaloneXbrlParser().Parse(Standalone()).Single().ConsolidatedCik.Should().BeNull();
    }

    [Theory]
    [InlineData("<xbrli:scenario><other>parent only</other></xbrli:scenario>")]
    [InlineData(
        "<xbrli:segment><xbrldi:typedMember dimension=\"issuer:SegmentAxis\"><segment>A</segment></xbrldi:typedMember></xbrli:segment>"
    )]
    public void Parse_QualifiedContextNeverProvesTheIssuerLei(string qualifier)
    {
        new InlineXbrlParser().Parse(Inline(qualifier)).Single().ConsolidatedLei.Should().BeNull();
        new StandaloneXbrlParser()
            .Parse(Standalone(qualifier))
            .Single()
            .ConsolidatedLei.Should()
            .BeNull();
    }

    [Fact]
    public void Parse_RepeatedContextIdDoesNotProveTheIssuerLei()
    {
        new InlineXbrlParser()
            .Parse(Inline().Replace(Context(), Context() + Context()))
            .Single()
            .ConsolidatedLei.Should()
            .BeNull();
    }

    [Fact]
    public void Parse_UnknownIdentifierSchemeDoesNotProveTheIssuerLei()
    {
        new InlineXbrlParser()
            .Parse(Inline().Replace(LeiScheme, "other"))
            .Single()
            .ConsolidatedLei.Should()
            .BeNull();
    }

    private const string Namespaces =
        "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" xmlns:ifrs-full=\"https://xbrl.ifrs.org/taxonomy/2022-03-24/ifrs-full\" xmlns:iso4217=\"http://www.xbrl.org/2003/iso4217\"";
    private const string Unit =
        "<xbrli:unit id=\"eur\"><xbrli:measure>iso4217:EUR</xbrli:measure></xbrli:unit>";

    private static string Context(string qualifier = "") =>
        $"<xbrli:context id=\"c1\"><xbrli:entity><xbrli:identifier scheme=\"{LeiScheme}\">{Lei}</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2024-01-01</xbrli:startDate><xbrli:endDate>2024-12-31</xbrli:endDate></xbrli:period>{qualifier}</xbrli:context>";

    private static string Inline(string qualifier = "") =>
        $"<html {Namespaces} xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body>{Context(qualifier)}{Unit}<ix:nonFraction name=\"ifrs-full:RevenueAndOperatingIncome\" contextRef=\"c1\" unitRef=\"eur\" scale=\"6\" decimals=\"-6\">37,742</ix:nonFraction></body></html>";

    private static string Standalone(string qualifier = "") =>
        $"<xbrli:xbrl {Namespaces}>{Context(qualifier)}{Unit}<ifrs-full:RevenueAndOperatingIncome contextRef=\"c1\" unitRef=\"eur\" decimals=\"-6\">37742000000</ifrs-full:RevenueAndOperatingIncome></xbrli:xbrl>";
}

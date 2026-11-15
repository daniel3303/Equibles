using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// CompareFinancialFact selects one stored year for every company, so every row covers the same
/// months; a company that names that year for the year it starts in is named in a footnote with
/// its own label, and a comparison without one carries no footnote.
/// </summary>
public class FinancialFactsToolsOwnNamingFootnoteTests
{
    private static EquityIssuer Issuer(string ticker, int? offset)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: ticker
        );
        issuer.FiscalYearLabelOffset = offset;
        return issuer;
    }

    private static (string Ticker, string Name, FinancialFact Fact) Row(
        EquityIssuer issuer,
        int fiscalYear
    ) =>
        (
            issuer.Presentation.Listing.Ticker,
            issuer.Name,
            new FinancialFact
            {
                EquityIssuerId = issuer.Id,
                Value = 1m,
                Unit = "USD",
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new DateOnly(fiscalYear - 1, 2, 3),
                PeriodEnd = new DateOnly(fiscalYear, 2, 1),
                FiscalYear = fiscalYear,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Form = DocumentType.TenK,
                FiledDate = new DateOnly(fiscalYear, 3, 20),
                AccessionNumber = $"acc-{issuer.Id}",
            }
        );

    [Fact]
    public void OwnNamingFootnote_StartYearCompany_IsNamedWithItsOwnLabel()
    {
        var homeDepot = Issuer("HD", -1);
        var walmart = Issuer("WMT", 0);

        var footnote = FinancialFactsTools.OwnNamingFootnote(
            [Row(homeDepot, 2026), Row(walmart, 2026)],
            [homeDepot, walmart]
        );

        footnote.Should().Contain("HD names this year FY2025");
        footnote.Should().NotContain("WMT");
    }

    [Fact]
    public void OwnNamingFootnote_NoStartYearCompany_IsEmpty()
    {
        var walmart = Issuer("WMT", 0);
        var unknown = Issuer("TGT", null);

        FinancialFactsTools
            .OwnNamingFootnote([Row(walmart, 2026), Row(unknown, 2026)], [walmart, unknown])
            .Should()
            .BeEmpty();
    }
}

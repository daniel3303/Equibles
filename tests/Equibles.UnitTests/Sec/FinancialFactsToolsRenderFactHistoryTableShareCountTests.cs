using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

// GetFinancialFact restates common-share counts with the same rule as per-share values, so the
// AVGO FY2023 balance-sheet count (already restated by the FY2024 10-K) stays 4.139B, the
// original filing's 413.9M becomes 4.139B, and a pre-split cover count moves by its as-of date.
public class FinancialFactsToolsRenderFactHistoryTableShareCountTests
{
    private static readonly Guid BalanceSheetCount = Guid.NewGuid();
    private static readonly Guid CoverCount = Guid.NewGuid();

    private static string Render(List<FinancialFact> facts)
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "RenderFactHistoryTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AVGO",
            Name: "Broadcom Inc."
        );
        var splits = new List<StockSplit>
        {
            new()
            {
                EquityListingId = stock.Presentation.EquityListingId,
                PriceSeriesTicker = "AVGO",
                EffectiveDate = new DateOnly(2024, 7, 15),
                Numerator = 10m,
                Denominator = 1m,
            },
        };
        var concepts = new Dictionary<Guid, FinancialConceptAliases.ConceptRef>
        {
            [BalanceSheetCount] = new(FactTaxonomy.UsGaap, "CommonStockSharesOutstanding"),
            [CoverCount] = new(FactTaxonomy.Dei, "EntityCommonStockSharesOutstanding"),
        };
        return (string)
            method.Invoke(
                null,
                ["shares-outstanding", stock, false, facts, facts.Count, null, splits, concepts]
            );
    }

    private static FinancialFact Count(
        Guid conceptId,
        decimal value,
        DateOnly asOf,
        DateOnly filed
    ) =>
        new()
        {
            FinancialConceptId = conceptId,
            Value = value,
            Unit = "shares",
            PeriodType = FactPeriodType.Instant,
            PeriodStart = asOf,
            PeriodEnd = asOf,
            FiscalYear = asOf.Year,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = filed,
            AccessionNumber = $"acc-{filed:yyyyMMdd}",
        };

    [Fact]
    public void RestatedComparative_IsNotMultipliedAgain()
    {
        var result = Render([
            Count(
                BalanceSheetCount,
                4_139_000_000m,
                new DateOnly(2023, 10, 29),
                new DateOnly(2024, 12, 20)
            ),
        ]);

        result.Should().Contain("4,139,000,000").And.NotContain("41,390,000,000");
    }

    [Fact]
    public void PreSplitFilings_AreRestatedByTheirFilingAndAsOfDates()
    {
        var result = Render([
            Count(
                BalanceSheetCount,
                413_900_000m,
                new DateOnly(2023, 10, 29),
                new DateOnly(2023, 12, 14)
            ),
            Count(CoverCount, 465_000_000m, new DateOnly(2024, 6, 5), new DateOnly(2024, 7, 20)),
        ]);

        result.Should().Contain("4,139,000,000").And.Contain("4,650,000,000");
        result.Should().Contain("common-share counts are split-adjusted");
    }
}

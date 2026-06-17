using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsPickBestFactDiscreteQuarterTests
{
    // A 10-Q tags each flow line twice under the same fiscal (year, period): the discrete
    // three-month quarter and the fiscal year-to-date (six months at Q2). For a quarterly
    // query the discrete figure must win — surfacing the YTD makes Q2 read as the H1 total
    // (GOOGL Q2 2025 revenue = $186.7B H1, not the $96.4B quarter). Both candidates share the
    // same filing date and accession (one filing), so without a span preference the filed-date
    // tiebreak is a wash and input order decides; the YTD is listed first here to pin that the
    // pick is the discrete quarter, not whichever happens to come first.
    [Fact]
    public void PickBestFact_QuarterGroupWithYtdAndDiscrete_PicksDiscreteQuarter()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var yearToDate = MakeFact(
            stockId,
            conceptId,
            value: 186_662m,
            periodStart: new DateOnly(2025, 1, 1),
            periodEnd: new DateOnly(2025, 6, 30)
        );
        var discreteQuarter = MakeFact(
            stockId,
            conceptId,
            value: 96_428m,
            periodStart: new DateOnly(2025, 4, 1),
            periodEnd: new DateOnly(2025, 6, 30)
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };

        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (FinancialFact)
            method!.Invoke(null, [new[] { yearToDate, discreteQuarter }, conceptPriority, false]);

        result.Value.Should().Be(96_428m, "the discrete quarter wins over the year-to-date span");
    }

    private static FinancialFact MakeFact(
        Guid stockId,
        Guid conceptId,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd
    ) =>
        new()
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
            Value = value,
            Unit = "USD",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            FiscalYear = 2025,
            FiscalPeriod = SecFiscalPeriod.Q2,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenQ,
            FiledDate = new DateOnly(2025, 7, 24),
            AccessionNumber = "0001652044-25-000062",
        };
}

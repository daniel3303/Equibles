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
            periodEnd: new DateOnly(2025, 6, 30),
            fiscalPeriod: SecFiscalPeriod.Q2
        );
        var discreteQuarter = MakeFact(
            stockId,
            conceptId,
            value: 96_428m,
            periodStart: new DateOnly(2025, 4, 1),
            periodEnd: new DateOnly(2025, 6, 30),
            fiscalPeriod: SecFiscalPeriod.Q2
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };

        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (FinancialFact)
            method!.Invoke(null, [new[] { yearToDate, discreteQuarter }, conceptPriority, false, null]);

        result.Value.Should().Be(96_428m, "the discrete quarter wins over the year-to-date span");
    }

    [Fact]
    public void PickBestFact_FullYearRejectsAnOverlongDuration()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var validAnnual = MakeFact(
            stockId,
            conceptId,
            value: 120m,
            periodStart: new DateOnly(2025, 1, 1),
            periodEnd: new DateOnly(2025, 12, 31),
            fiscalPeriod: SecFiscalPeriod.FullYear
        );
        var inceptionToDate = MakeFact(
            stockId,
            conceptId,
            value: 9_999m,
            periodStart: new DateOnly(2020, 1, 1),
            periodEnd: new DateOnly(2025, 12, 31),
            fiscalPeriod: SecFiscalPeriod.FullYear
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };
        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var picked = (FinancialFact)
            method!.Invoke(null, [new[] { inceptionToDate, validAnnual }, conceptPriority, false, null]);
        var rejected = method.Invoke(null, [new[] { inceptionToDate }, conceptPriority, false, null]);

        picked.Should().BeSameAs(validAnnual);
        rejected.Should().BeNull("a multi-year duration is not a fiscal-year fact");
    }

    [Fact]
    public void PickBestFact_QuarterStampRejectsAnOverlongDuration()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var validQuarter = MakeFact(
            stockId,
            conceptId,
            value: 25m,
            periodStart: new DateOnly(2025, 4, 1),
            periodEnd: new DateOnly(2025, 6, 30),
            fiscalPeriod: SecFiscalPeriod.Q2
        );
        var inceptionToDate = MakeFact(
            stockId,
            conceptId,
            value: 9_999m,
            periodStart: new DateOnly(2003, 5, 13),
            periodEnd: new DateOnly(2025, 6, 30),
            fiscalPeriod: SecFiscalPeriod.Q2
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };
        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var picked = (FinancialFact)
            method!.Invoke(null, [new[] { inceptionToDate, validQuarter }, conceptPriority, false, null]);
        var rejected = method.Invoke(null, [new[] { inceptionToDate }, conceptPriority, false, null]);

        picked.Should().BeSameAs(validQuarter);
        rejected.Should().BeNull("a fiscal stamp cannot turn an inception duration into a quarter");
    }

    [Fact]
    public void PickBestFact_QuarterStampRejectsYearToDateFallback()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var yearToDate = MakeFact(
            stockId,
            conceptId,
            value: 60m,
            periodStart: new DateOnly(2025, 1, 1),
            periodEnd: new DateOnly(2025, 6, 30),
            fiscalPeriod: SecFiscalPeriod.Q2
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };
        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var rejected = method!.Invoke(null, [new[] { yearToDate }, conceptPriority, false, null]);

        rejected.Should().BeNull("a six-month YTD span is not a discrete fiscal quarter");
    }

    private static FinancialFact MakeFact(
        Guid stockId,
        Guid conceptId,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd,
        SecFiscalPeriod fiscalPeriod
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
            FiscalPeriod = fiscalPeriod,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenQ,
            FiledDate = new DateOnly(2025, 7, 24),
            AccessionNumber = "0001652044-25-000062",
        };
}

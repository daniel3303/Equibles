using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialFactSeriesShaperPickBestFactAnnualReportTests
{
    private static readonly Guid Revenues = Guid.NewGuid();

    private static FinancialFact Year(
        decimal millions,
        DocumentType form,
        DateOnly filed,
        string accession
    ) =>
        new()
        {
            FinancialConceptId = Revenues,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            Value = millions * 1_000_000m,
            FiscalYear = 2025,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = form,
            FiledDate = filed,
            AccessionNumber = accession,
        };

    // Comfort Systems' first-quarter 10-Q tagged fiscal 2025 Revenues as 1,831.3M, filed after its
    // 10-K's 9,101.6M: the later filing does not restate the year.
    [Fact]
    public void PickBestFact_YearALaterQuarterlyReportRepeats_PicksTheAnnualReport()
    {
        var annual = Year(
            9_101.6m,
            DocumentType.TenK,
            new DateOnly(2026, 2, 19),
            "0001035983-26-000008"
        );
        var quarterly = Year(
            1_831.3m,
            DocumentType.TenQ,
            new DateOnly(2026, 4, 23),
            "0001035983-26-000021"
        );

        var picked = FinancialFactSeriesShaper.PickBestFact(
            [quarterly, annual],
            new Dictionary<Guid, int> { [Revenues] = 0 }
        );

        picked.Should().BeSameAs(annual);
    }
}

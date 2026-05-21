using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceCollapseToNaturalKeyLatestFiledWinsTests
{
    // CollapseToNaturalKey's stated contract (and the contract spelled out on
    // the FinancialFact entity itself, "the most-recently-reported value for a
    // period is the one with the latest FiledDate") is: when two rows share the
    // unique-index key (CommonStockId, FinancialConceptId, Unit, PeriodStart,
    // PeriodEnd, AccessionNumber), collapse to the one with the LATEST
    // FiledDate, dropping the earlier-filed value. A refactor flipping the
    // OrderByDescending to OrderBy, or replacing .First() with .Last(), would
    // silently keep the earlier (now-superseded) value instead — corrupting
    // restated facts on every subsequent import.
    [Fact]
    public void CollapseToNaturalKey_TwoRowsSameKeyDifferentFiledDate_KeepsLatestFiled()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var accession = "0000320193-24-000123";

        var earlier = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
            Unit = "USD",
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 3, 31),
            AccessionNumber = accession,
            Value = 100m,
            FiledDate = new DateOnly(2024, 5, 1),
            Form = DocumentType.TenK,
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
        };
        var later = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
            Unit = "USD",
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 3, 31),
            AccessionNumber = accession,
            Value = 200m,
            FiledDate = new DateOnly(2024, 6, 1),
            Form = DocumentType.TenK,
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
        };

        var method = typeof(FinancialFactsImportService).GetMethod(
            "CollapseToNaturalKey",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result =
            (List<FinancialFact>)method.Invoke(null, [new List<FinancialFact> { earlier, later }]);

        result.Should().ContainSingle();
        result[0].Value.Should().Be(200m);
        result[0].FiledDate.Should().Be(new DateOnly(2024, 6, 1));
    }
}

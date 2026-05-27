using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceCollapseToNaturalKeyDistinctAccessionTests
{
    [Fact]
    public void CollapseToNaturalKey_TwoRowsSameFactDifferentAccession_KeepsBothRows()
    {
        // CollapseToNaturalKey's group-by tuple intentionally includes
        // AccessionNumber: the same accounting fact (CommonStockId, Concept,
        // Unit, PeriodStart, PeriodEnd) can legitimately appear under two
        // accessions — once in the original 10-K (accession A) and again in a
        // restated 10-K/A (accession B). Both rows must survive collapse so
        // each is linked to its own filing document and an auditor can trace
        // the restatement history. Sibling pin (LatestFiledWins) covers
        // shrinking 2 → 1 when the key matches; this is the inverse contract:
        // when AccessionNumber differs, the rows MUST NOT collapse. A
        // refactor that drops AccessionNumber from the natural key (perhaps
        // "AccessionNumber isn't really part of the fact identity") would
        // silently merge restated facts and lose every original-filing row.
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();

        var rowA = MakeFact(
            stockId,
            conceptId,
            "0000320193-24-000123",
            value: 100m,
            filed: new DateOnly(2024, 5, 1)
        );
        var rowB = MakeFact(
            stockId,
            conceptId,
            "0000320193-24-999999",
            value: 105m,
            filed: new DateOnly(2024, 8, 15)
        );

        var method = typeof(FinancialFactsImportService).GetMethod(
            "CollapseToNaturalKey",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result =
            (List<FinancialFact>)method!.Invoke(null, [new List<FinancialFact> { rowA, rowB }]);

        result.Should().HaveCount(2);
        result
            .Select(r => r.AccessionNumber)
            .Should()
            .BeEquivalentTo(["0000320193-24-000123", "0000320193-24-999999"]);
    }

    private static FinancialFact MakeFact(
        Guid stockId,
        Guid conceptId,
        string accession,
        decimal value,
        DateOnly filed
    ) =>
        new()
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
            Unit = "USD",
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 3, 31),
            AccessionNumber = accession,
            Value = value,
            FiledDate = filed,
            Form = DocumentType.TenK,
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
        };
}

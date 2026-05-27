using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsPickBestFactConceptPriorityTests
{
    [Fact]
    public void PickBestFact_HigherPriorityConceptOlderFiling_BeatsLowerPriorityNewerFiling()
    {
        // PickBestFact's primary sort key is `OrderBy(f => conceptPriority[f.FinancialConceptId])`
        // — the alias's preferred concept always wins, even against a more
        // recently-filed but lower-priority synonym. Existing siblings pin
        // the date and accession tiebreaks (#2353, #2354, #2355) but they
        // all share the same conceptId, so the first OrderBy never actually
        // narrows the candidate pool there. This pin forces the priority
        // hop to do the work: two facts with DIFFERENT conceptIds — the
        // higher-priority alias (index 0) is OLDER, the lower-priority
        // alias (index 1) is NEWER. The higher-priority MUST win.
        // A refactor that drops the OrderBy(conceptPriority) primary key
        // (or flips it to OrderByDescending under "newer is better") would
        // surface the lower-priority synonym to every LLM caller — e.g.
        // returning "RevenueFromContractWithCustomerExcludingAssessedTax"
        // when the alias prefers the canonical "Revenues".
        var stockId = Guid.NewGuid();
        var primaryConceptId = Guid.NewGuid();
        var synonymConceptId = Guid.NewGuid();
        var primary = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = primaryConceptId,
            Value = 100m,
            FiledDate = new DateOnly(2024, 5, 1),
            AccessionNumber = "0000320193-24-000100",
            Unit = "USD",
            PeriodEnd = new DateOnly(2024, 3, 31),
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenK,
        };
        var synonym = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = synonymConceptId,
            Value = 200m,
            FiledDate = new DateOnly(2024, 9, 15),
            AccessionNumber = "0000320193-24-000999",
            Unit = "USD",
            PeriodEnd = new DateOnly(2024, 3, 31),
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenK,
        };
        var conceptPriority = new Dictionary<Guid, int>
        {
            [primaryConceptId] = 0,
            [synonymConceptId] = 1,
        };

        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (FinancialFact)
            method!.Invoke(null, [new[] { synonym, primary }, conceptPriority, false]);

        result.Value.Should().Be(100m);
        result.FinancialConceptId.Should().Be(primaryConceptId);
    }
}

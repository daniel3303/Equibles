using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsPickBestFactAccessionTiebreakTests
{
    [Fact]
    public void PickBestFact_TwoSameDayAmendmentsDefaultMode_AccessionTiebreakPicksLexicalLatest()
    {
        // PickBestFact's source comment (FinancialFactsTools.cs:340-342) is
        // explicit: "AccessionNumber is the stable final tiebreak for same-day
        // amendments (Postgres has no implicit row order)." Existing pins
        // assert the asOriginallyReported flag flips overall ordering, but
        // not that AccessionNumber acts as the FINAL tiebreak when filed
        // dates collide. SEC routinely processes a 10-K and a same-day
        // 10-K/A amendment that share FiledDate — without the AccessionNumber
        // .ThenByDescending, the row order would be Postgres-iteration-
        // dependent and produce non-deterministic LLM responses. Pin:
        // two facts with identical FiledDate but different accessions.
        // Default mode → DESCENDING accession → the lexically-LARGER
        // accession wins. The two accessions are chosen so a string vs
        // numeric comparison would diverge.
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var earlierAcc = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
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
        var laterAcc = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
            Value = 200m,
            FiledDate = new DateOnly(2024, 5, 1),
            AccessionNumber = "0000320193-24-000999",
            Unit = "USD",
            PeriodEnd = new DateOnly(2024, 3, 31),
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenK,
        };
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };

        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (FinancialFact)
            method!.Invoke(null, [new[] { earlierAcc, laterAcc }, conceptPriority, false]);

        result.Value.Should().Be(200m);
        result.AccessionNumber.Should().Be("0000320193-24-000999");
    }
}

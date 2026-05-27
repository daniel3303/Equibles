using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsPickBestFactAsOriginallyReportedTests
{
    [Fact]
    public void PickBestFact_AsOriginallyReportedTrue_PicksEarliestFiledFact()
    {
        // PickBestFact's contract (FinancialFactsTools.cs:340-355) flips
        // filed-date / accession ordering based on `asOriginallyReported`:
        // default = latest-filed (the restated value); true = earliest-filed
        // (the original, pre-restatement value). The flag is the entire
        // reason the helper exists — restatement history is auditor-relevant
        // and the LLM caller explicitly requests "as originally reported"
        // when they want to compare the original 10-K to a later 10-K/A.
        // A refactor that drops the flag handling (always returning the
        // latest filing) would compile, pass any default-mode test, and
        // silently lie about the original value — surfacing the restatement
        // instead. Pin: two same-concept facts, second filed later, with
        // asOriginallyReported=true returns the FIRST (earliest) fact.
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var original = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
            Value = 1_000m,
            FiledDate = new DateOnly(2024, 5, 1),
            AccessionNumber = "0000320193-24-000123",
            Unit = "USD",
            PeriodEnd = new DateOnly(2024, 3, 31),
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenK,
        };
        var restated = new FinancialFact
        {
            CommonStockId = stockId,
            FinancialConceptId = conceptId,
            Value = 1_200m,
            FiledDate = new DateOnly(2024, 9, 15),
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
            method!.Invoke(null, [new[] { restated, original }, conceptPriority, true]);

        result.Value.Should().Be(1_000m);
        result.FiledDate.Should().Be(new DateOnly(2024, 5, 1));
    }
}

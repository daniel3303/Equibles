using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialFactSeriesShaperPickBestFactLatestReportedTests
{
    [Fact]
    public void PickBestFact_DefaultMode_PicksLatestFiledRestatedFact()
    {
        // Sibling to AsOriginallyReportedTests. With `asOriginallyReported`
        // omitted (default false), the ordering must be DESCENDING filed
        // date / accession — the LATEST restated value wins, per
        // FinancialFactSeriesShaper. A refactor that inverts the
        // default sense (e.g. flipping the default param to true, or
        // swapping the two branch bodies) would silently surface original
        // pre-restatement values to every consumer that didn't pass the
        // flag — corrupting "latest-known" balance-sheet lookups for
        // restated companies. Pin: two facts, same concept, default call
        // returns the later-filed (restated) fact.
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var original = new FinancialFact
        {
            EquityIssuerId = stockId,
            FinancialConceptId = conceptId,
            Value = 1_000m,
            FiledDate = new DateOnly(2024, 5, 1),
            AccessionNumber = "0000320193-24-000123",
            Unit = "USD",
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 3, 31),
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenK,
        };
        var restated = new FinancialFact
        {
            EquityIssuerId = stockId,
            FinancialConceptId = conceptId,
            Value = 1_200m,
            FiledDate = new DateOnly(2024, 9, 15),
            AccessionNumber = "0000320193-24-000999",
            Unit = "USD",
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 3, 31),
            FiscalYear = 2024,
            FiscalPeriod = SecFiscalPeriod.Q1,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenK,
        };
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };

        var result = FinancialFactSeriesShaper.PickBestFact(
            new[] { original, restated },
            conceptPriority,
            false,
            null
        );

        result.Value.Should().Be(1_200m);
        result.FiledDate.Should().Be(new DateOnly(2024, 9, 15));
    }
}

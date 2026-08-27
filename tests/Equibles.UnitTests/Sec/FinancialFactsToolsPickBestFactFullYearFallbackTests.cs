using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

// A FullYear bucket with no annual-span fact used to fall back to ANY duration up to
// the annual ceiling, publishing six/nine-month year-to-date spans (and lone discrete
// quarters the resolver could not classify) as the annual value. The fallback now
// admits only a fiscal-calendar transition stub the surrounding annual windows
// corroborate on both sides, and fails closed for callers with no corroboration
// context (the cross-company comparison, which loads a single fiscal slice).
public class FinancialFactsToolsPickBestFactFullYearFallbackTests
{
    [Fact]
    public void PickBestFact_FullYearBucketWithOnlyYtd_ReturnsNullDespiteContext()
    {
        var conceptId = Guid.NewGuid();
        var yearToDate = MakeFact(
            conceptId,
            value: 60m,
            periodStart: new DateOnly(2026, 1, 1),
            periodEnd: new DateOnly(2026, 6, 30)
        );
        // The company's ordinary calendar years: the prior one abuts the YTD's start,
        // but nothing starts where the YTD ends — no end-side seam.
        var context = new[]
        {
            MakeFact(
                conceptId,
                value: 100m,
                periodStart: new DateOnly(2025, 1, 1),
                periodEnd: new DateOnly(2025, 12, 31)
            ),
            yearToDate,
        };
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };

        var rejected = InvokePickBestFact([yearToDate], conceptPriority, context);

        rejected.Should().BeNull("a year-to-date span must never publish as the annual value");
    }

    [Fact]
    public void PickBestFact_CorroboratedTransitionStub_IsKept()
    {
        var conceptId = Guid.NewGuid();
        var stub = MakeFact(
            conceptId,
            value: 45m,
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 6, 30)
        );
        var context = new[]
        {
            MakeFact(
                conceptId,
                value: 100m,
                periodStart: new DateOnly(2023, 1, 1),
                periodEnd: new DateOnly(2023, 12, 31)
            ),
            stub,
            MakeFact(
                conceptId,
                value: 110m,
                periodStart: new DateOnly(2024, 7, 1),
                periodEnd: new DateOnly(2025, 6, 30)
            ),
        };
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };

        var picked = InvokePickBestFact([stub], conceptPriority, context);

        picked.Should().BeSameAs(stub, "both neighbouring annual windows corroborate the stub");
    }

    [Fact]
    public void PickBestFact_NoCorroborationContext_FailsClosed()
    {
        var conceptId = Guid.NewGuid();
        var stub = MakeFact(
            conceptId,
            value: 45m,
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 6, 30)
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };

        var rejected = InvokePickBestFact([stub], conceptPriority, context: null);

        rejected
            .Should()
            .BeNull("without context a sub-annual FullYear duration cannot be proven a stub");
    }

    private static FinancialFact InvokePickBestFact(
        FinancialFact[] group,
        Dictionary<Guid, int> conceptPriority,
        FinancialFact[] context
    )
    {
        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return (FinancialFact)method!.Invoke(null, [group, conceptPriority, false, context]);
    }

    private static FinancialFact MakeFact(
        Guid conceptId,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd
    ) =>
        new()
        {
            CommonStockId = Guid.NewGuid(),
            FinancialConceptId = conceptId,
            Value = value,
            Unit = "USD",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            FiscalYear = periodEnd.Year,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TenK,
            FiledDate = periodEnd.AddMonths(2),
            AccessionNumber = "0000000000-26-000001",
        };
}

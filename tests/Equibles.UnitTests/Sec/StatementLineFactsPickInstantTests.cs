using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class StatementLineFactsPickInstantTests
{
    [Theory]
    [InlineData(FactPeriodType.Instant)]
    [InlineData(FactPeriodType.Duration)]
    public void PickCurrentlyReported_YearEndRecastInQuarterlyFiling_UsesCurrentPeriodicBasis(
        FactPeriodType periodType
    )
    {
        // The same 2025-12-31 balance date is recast in the later quarterly filing.
        // A later current-report disclosure and an earlier balance date cannot override it.
        var date = new DateOnly(2025, 12, 31);
        var annual = Instant(date, 1590m);
        annual.Form = DocumentType.TenK;
        annual.FiledDate = new DateOnly(2026, 2, 12);
        var recast = Instant(date, 1409m);
        recast.Form = DocumentType.TenQ;
        recast.FiledDate = new DateOnly(2026, 8, 6);
        var currentReport = Instant(date, 999m);
        currentReport.Form = DocumentType.EightK;
        currentReport.FiledDate = new DateOnly(2026, 9, 1);
        var priorDate = Instant(new DateOnly(2025, 9, 30), 555m);
        priorDate.Form = DocumentType.TenQ;
        priorDate.FiledDate = new DateOnly(2026, 9, 20);
        var facts = new[] { currentReport, annual, priorDate, recast };
        foreach (var fact in facts)
            fact.PeriodType = periodType;

        StatementLineFacts
            .PickCurrentlyReported(facts, SecFiscalPeriod.FullYear)
            .Should()
            .BeSameAs(recast);
    }

    // Instants span zero days, so they qualify for every period — correct for a balance
    // sheet, but it also admits a flow concept's point disclosure into an annual bucket,
    // where its later date beats the year's own column on period end.
    [Fact]
    public void PickCurrentlyReported_BucketWithALaterInstant_PicksTheDuration()
    {
        var fiscalYear = Duration(new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), 0m);
        var paymentDate = Instant(new DateOnly(2023, 1, 12), 12_400_000m);

        var picked = StatementLineFacts.PickCurrentlyReported(
            [paymentDate, fiscalYear],
            SecFiscalPeriod.FullYear
        );

        picked
            .Should()
            .BeSameAs(fiscalYear, "a period a duration measures is not read off an instant");
    }

    // The same poison tagged as a zero-day duration rather than an instant.
    [Fact]
    public void PickCurrentlyReported_BucketWithALaterZeroDayDuration_PicksTheMeasuredSpan()
    {
        var fiscalYear = Duration(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 0m);
        var paymentDate = Duration(new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 9), 80_000_000m);

        var picked = StatementLineFacts.PickCurrentlyReported(
            [paymentDate, fiscalYear],
            SecFiscalPeriod.FullYear
        );

        picked.Should().BeSameAs(fiscalYear);
    }

    // The control: an instant-only bucket is every balance-sheet concept, and the rule above
    // must never make one of those lines absent.
    [Fact]
    public void PickCurrentlyReported_InstantsOnly_PicksTheYearEndInstant()
    {
        var comparative = Instant(new DateOnly(2021, 12, 31), 900m);
        var yearEnd = Instant(new DateOnly(2022, 12, 31), 1_000m);

        var picked = StatementLineFacts.PickCurrentlyReported(
            [comparative, yearEnd],
            SecFiscalPeriod.FullYear
        );

        picked.Should().BeSameAs(yearEnd);
    }

    private static FinancialFact Instant(DateOnly instant, decimal value)
    {
        var fact = Duration(instant, instant, value);
        fact.PeriodType = FactPeriodType.Instant;
        return fact;
    }

    private static FinancialFact Duration(DateOnly start, DateOnly end, decimal value) =>
        new()
        {
            EquityIssuerId = Guid.NewGuid(),
            FinancialConceptId = Guid.NewGuid(),
            Value = value,
            Unit = "USD",
            PeriodStart = start,
            PeriodEnd = end,
            FiscalYear = 2022,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TwentyF,
            FiledDate = new DateOnly(2024, 4, 24),
            AccessionNumber = "0001437749-24-012913",
        };
}

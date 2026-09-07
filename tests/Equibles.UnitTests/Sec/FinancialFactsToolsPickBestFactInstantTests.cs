using System.Reflection;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsToolsPickBestFactInstantTests
{
    // Instants qualify for every period because a balance-sheet line has no duration. That
    // also let a flow concept's declaration/record-date instant into an annual bucket, where
    // it ends after the fiscal year and so beat the year's own column on period end: a filer
    // that tagged the 2023-01-12 payment of its first dividend as FY2022 published $12.4M of
    // dividends for a year it paid none.
    [Fact]
    public void PickBestFact_AnnualGroupWithAnInstantAfterThePeriod_PicksTheDuration()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var fiscalYear = MakeFact(
            stockId,
            conceptId,
            value: 0m,
            periodStart: new DateOnly(2022, 1, 1),
            periodEnd: new DateOnly(2022, 12, 31),
            fiscalPeriod: SecFiscalPeriod.FullYear
        );
        var paymentDate = MakeInstant(
            stockId,
            conceptId,
            value: 12_400_000m,
            instant: new DateOnly(2023, 1, 12),
            fiscalPeriod: SecFiscalPeriod.FullYear
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };
        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var picked = (FinancialFact)
            method!.Invoke(null, [new[] { paymentDate, fiscalYear }, conceptPriority, false, null]);

        picked
            .Should()
            .BeSameAs(fiscalYear, "a period a duration measures is not read off an instant");
    }

    // The control: a balance-sheet bucket holds only instants, so the period-end instant still
    // wins over a re-stated comparative one. The rule above must never make a balance line absent.
    [Fact]
    public void PickBestFact_AnnualGroupOfInstantsOnly_PicksTheYearEndInstant()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var comparative = MakeInstant(
            stockId,
            conceptId,
            value: 900m,
            instant: new DateOnly(2021, 12, 31),
            fiscalPeriod: SecFiscalPeriod.FullYear
        );
        var yearEnd = MakeInstant(
            stockId,
            conceptId,
            value: 1_000m,
            instant: new DateOnly(2022, 12, 31),
            fiscalPeriod: SecFiscalPeriod.FullYear
        );
        var conceptPriority = new Dictionary<Guid, int> { [conceptId] = 0 };
        var method = typeof(FinancialFactsTools).GetMethod(
            "PickBestFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var picked = (FinancialFact)
            method!.Invoke(null, [new[] { comparative, yearEnd }, conceptPriority, false, null]);

        picked.Should().BeSameAs(yearEnd);
    }

    private static FinancialFact MakeInstant(
        Guid stockId,
        Guid conceptId,
        decimal value,
        DateOnly instant,
        SecFiscalPeriod fiscalPeriod
    )
    {
        var fact = MakeFact(stockId, conceptId, value, instant, instant, fiscalPeriod);
        fact.PeriodType = FactPeriodType.Instant;
        return fact;
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
            FiscalYear = 2022,
            FiscalPeriod = fiscalPeriod,
            PeriodType = FactPeriodType.Duration,
            Form = DocumentType.TwentyF,
            FiledDate = new DateOnly(2024, 4, 24),
            AccessionNumber = "0001437749-24-012913",
        };
}

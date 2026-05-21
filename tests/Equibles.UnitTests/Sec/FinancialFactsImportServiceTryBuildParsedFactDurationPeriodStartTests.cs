using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryBuildParsedFactDurationPeriodStartTests
{
    // Sibling to FinancialFactsImportServiceTryBuildParsedFactInstantPeriodStartTests.
    // That pin covers `Start = null` → PeriodType = Instant + PeriodStart = End.
    // This pin covers the other arm of the same XML-doc contract: when the wire
    // payload carries a non-null Start (the "duration" / income-statement shape),
    // PeriodType must be Duration and PeriodStart must be value.Start (not value.End).
    //
    // The risk this catches and the sibling does NOT:
    //   - A refactor that flips `value.Start ?? value.End` to `value.End ?? value.Start`
    //     leaves the instant case correct (Start is null, so both forms collapse to End)
    //     while silently rewriting PeriodStart on every income-statement / cash-flow fact
    //     to the period's END date. The composite unique index across (CommonStockId,
    //     FinancialConceptId, Unit, PeriodStart, PeriodEnd, AccessionNumber) would then
    //     collapse every duration concept down to a single row, masking restatements
    //     that legitimately differ on PeriodStart.
    //   - A refactor that flips `value.Start == null` to `!=` flips PeriodType on every
    //     fact and would be caught by the sibling — but the existing pin alone doesn't
    //     guarantee the projection is correct for the non-null branch.
    [Fact]
    public void TryBuildParsedFact_DurationFactWithStart_PeriodStartIsValueStartAndPeriodTypeIsDuration()
    {
        var serviceType = typeof(FinancialFactsImportService);
        var parsedFactType = serviceType.GetNestedType("ParsedFact", BindingFlags.NonPublic);

        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL" };
        var periodStartInput = new DateOnly(2024, 1, 1);
        var periodEndInput = new DateOnly(2024, 12, 31);
        var value = new CompanyFactValue
        {
            Start = periodStartInput,
            End = periodEndInput,
            Val = 1_000m,
            Accn = "0000320193-24-000456",
            Fy = 2024,
            Fp = "FY",
            Form = "10-K",
            Filed = new DateOnly(2025, 1, 15),
        };

        var method = serviceType.GetMethod(
            "TryBuildParsedFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var parsed = method.Invoke(
            null,
            [FactTaxonomy.UsGaap, "Revenues", "Revenues", "USD", value, stock]
        );

        parsed.Should().NotBeNull();
        var periodType = (FactPeriodType)parsedFactType.GetProperty("PeriodType").GetValue(parsed);
        var periodStart = (DateOnly)parsedFactType.GetProperty("PeriodStart").GetValue(parsed);
        var periodEnd = (DateOnly)parsedFactType.GetProperty("PeriodEnd").GetValue(parsed);

        periodType.Should().Be(FactPeriodType.Duration);
        periodStart.Should().Be(periodStartInput);
        periodEnd.Should().Be(periodEndInput);
    }
}

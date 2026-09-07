using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class StatementLineFactsAnchorTests
{
    // The statement is anchored to one reporting endpoint before any line is picked, so a
    // point disclosure filed under the fiscal stamp used to decide it for the whole
    // statement: OPRA tagged the 2023-01-12 payment of its first dividend as FY2022, the
    // maximum period end moved outside the fiscal year, and every 2022-12-31 line was
    // dropped — the cash-flow statement rendered that one instant and nothing else.
    [Fact]
    public void AnchorToLatestPeriodEnd_FlowStatementWithALaterInstant_AnchorsOnTheDurations()
    {
        var revenue = Duration(new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), 1_000m);
        var operatingCashFlow = Duration(
            new DateOnly(2022, 1, 1),
            new DateOnly(2022, 12, 31),
            500m
        );
        var cashAtEndOfPeriod = Instant(new DateOnly(2022, 12, 31), 250m);
        var dividendPaymentDate = Instant(new DateOnly(2023, 1, 12), 12_400_000m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd([
            revenue,
            operatingCashFlow,
            cashAtEndOfPeriod,
            dividendPaymentDate,
        ]);

        anchored
            .Should()
            .BeEquivalentTo(
                [revenue, operatingCashFlow, cashAtEndOfPeriod],
                "the statement ends where its durations end, and the period-end instant "
                    + "shares that date"
            );
        anchored.Should().NotContain(dividendPaymentDate);
    }

    // The control: a balance sheet is every-fact-an-instant, so it must keep anchoring on
    // its latest instant. Preferring durations there would leave nothing to anchor on.
    [Fact]
    public void AnchorToLatestPeriodEnd_AllInstantStatement_AnchorsOnTheLatestInstant()
    {
        var comparative = Instant(new DateOnly(2021, 12, 31), 900m);
        var currentAssets = Instant(new DateOnly(2022, 12, 31), 1_000m);
        var currentLiabilities = Instant(new DateOnly(2022, 12, 31), 400m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd([
            comparative,
            currentAssets,
            currentLiabilities,
        ]);

        anchored.Should().BeEquivalentTo([currentAssets, currentLiabilities]);
    }

    // A comparative duration under the same fiscal stamp must still lose to the current one.
    [Fact]
    public void AnchorToLatestPeriodEnd_ComparativeDuration_AnchorsOnTheLatestDuration()
    {
        var priorYear = Duration(new DateOnly(2021, 1, 1), new DateOnly(2021, 12, 31), 800m);
        var currentYear = Duration(new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), 1_000m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd([priorYear, currentYear]);

        anchored.Should().BeEquivalentTo([currentYear]);
    }

    [Fact]
    public void AnchorToLatestPeriodEnd_NoFacts_ReturnsEmpty()
    {
        StatementLineFacts.AnchorToLatestPeriodEnd([]).Should().BeEmpty();
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
            CommonStockId = Guid.NewGuid(),
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

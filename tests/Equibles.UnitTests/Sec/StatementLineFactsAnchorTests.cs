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

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [revenue, operatingCashFlow, cashAtEndOfPeriod, dividendPaymentDate],
            SecFiscalPeriod.FullYear
        );

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

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [comparative, currentAssets, currentLiabilities],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([currentAssets, currentLiabilities]);
    }

    // A comparative duration under the same fiscal stamp must still lose to the current one.
    [Fact]
    public void AnchorToLatestPeriodEnd_ComparativeDuration_AnchorsOnTheLatestDuration()
    {
        var priorYear = Duration(new DateOnly(2021, 1, 1), new DateOnly(2021, 12, 31), 800m);
        var currentYear = Duration(new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), 1_000m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [priorYear, currentYear],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([currentYear]);
    }

    // The same poison arrives tagged as a zero-day DURATION, not an instant: DHC filed its
    // 2026-01-09 dividend payment that way under FY2025, and gating on PeriodType alone let
    // it anchor the statement past the 2025-12-31 year end.
    [Fact]
    public void AnchorToLatestPeriodEnd_FlowStatementWithALaterZeroDayDuration_AnchorsOnTheMeasuredSpans()
    {
        var operatingCashFlow = Duration(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            500m
        );
        var dividendPaymentDate = Duration(
            new DateOnly(2026, 1, 9),
            new DateOnly(2026, 1, 9),
            80_000_000m
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [operatingCashFlow, dividendPaymentDate],
            SecFiscalPeriod.FullYear
        );

        anchored
            .Should()
            .BeEquivalentTo(
                [operatingCashFlow],
                "a zero-day duration is a point disclosure, whatever its PeriodType says"
            );
    }

    // The control for the zero-day shape: a statement of nothing but points must still
    // anchor on its latest point rather than return empty.
    [Fact]
    public void AnchorToLatestPeriodEnd_ZeroDayDurationsOnly_AnchorsOnTheLatestPoint()
    {
        var earlier = Duration(new DateOnly(2025, 6, 30), new DateOnly(2025, 6, 30), 10m);
        var latest = Duration(new DateOnly(2025, 12, 31), new DateOnly(2025, 12, 31), 20m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [earlier, latest],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([latest]);
    }

    [Fact]
    public void AnchorToLatestPeriodEnd_NoFacts_ReturnsEmpty()
    {
        StatementLineFacts.AnchorToLatestPeriodEnd([], SecFiscalPeriod.FullYear).Should().BeEmpty();
    }

    // A span that measures something OTHER than the period drags the anchor off it just as a
    // point does: AZO's FY2021 Q1 carried a dimensional 167-day NetIncomeLoss ending
    // 2021-02-13, which anchored the quarter on a date its own 83-day lines could not serve,
    // and the statement rendered empty.
    [Fact]
    public void AnchorToLatestPeriodEnd_QuarterWithALongerNonConformingSpan_AnchorsOnTheQuarter()
    {
        var quarter = Duration(new DateOnly(2020, 8, 30), new DateOnly(2020, 11, 21), 500m);
        var twoQuarters = Duration(new DateOnly(2020, 8, 30), new DateOnly(2021, 2, 13), 900m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [quarter, twoQuarters],
            SecFiscalPeriod.Q1
        );

        anchored.Should().BeEquivalentTo([quarter]);
    }

    // A payment filed as a short WINDOW rather than a single day: BHM tagged its dividend
    // 2026-01-01 to 2026-01-15 under FY2025, which has a span and so passes a bare span rule.
    [Fact]
    public void AnchorToLatestPeriodEnd_FullYearWithALaterShortWindow_AnchorsOnTheFiscalYear()
    {
        var fiscalYear = Duration(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), 500m);
        var paymentWindow = Duration(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 80m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [fiscalYear, paymentWindow],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([fiscalYear]);
    }

    // The fallback: a statement whose facts measure no conforming span still anchors rather
    // than emptying — here on the latest measured span.
    [Fact]
    public void AnchorToLatestPeriodEnd_NoConformingSpan_FallsBackToTheLatestSpan()
    {
        var earlier = Duration(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), 10m);
        var later = Duration(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 20m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [earlier, later],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([later]);
    }

    // A dimensional span of conforming LENGTH is still the wrong period: HOFT's FY2025
    // carried a 368-day trailing-twelve-month window ending 2025-05-04 against its real year
    // ending 2025-02-02, and length alone cannot tell the two apart.
    [Fact]
    public void AnchorToLatestPeriodEnd_DimensionalTrailingWindow_AnchorsOnTheConsolidatedYear()
    {
        var fiscalYear = Duration(new DateOnly(2024, 1, 29), new DateOnly(2025, 2, 2), 500m);
        var trailingWindow = Dimensional(
            Duration(new DateOnly(2024, 5, 1), new DateOnly(2025, 5, 4), 900m)
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [fiscalYear, trailingWindow],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([fiscalYear]);
    }

    // The same rung fixes a LATER period stamped into this bucket: GIS's fiscal year ends in
    // May, so its FY2020 Q2 is the quarter ending 2019-11-24, and a dimensional FY2021 Q1
    // span ending 2020-08-30 sat in the same bucket and won on date.
    [Fact]
    public void AnchorToLatestPeriodEnd_DimensionalLaterQuarter_AnchorsOnTheConsolidatedQuarter()
    {
        var ownQuarter = Duration(new DateOnly(2019, 8, 26), new DateOnly(2019, 11, 24), 500m);
        var laterQuarter = Dimensional(
            Duration(new DateOnly(2020, 6, 1), new DateOnly(2020, 8, 30), 900m)
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [ownQuarter, laterQuarter],
            SecFiscalPeriod.Q2
        );

        anchored.Should().BeEquivalentTo([ownQuarter]);
    }

    // The control: a filer that tags a period ONLY dimensionally must still render it, so the
    // consolidated rung falls through rather than emptying the statement.
    [Fact]
    public void AnchorToLatestPeriodEnd_DimensionalOnly_StillAnchorsOnTheLatestConformingSpan()
    {
        var earlier = Dimensional(
            Duration(new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31), 10m)
        );
        var latest = Dimensional(
            Duration(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), 20m)
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [earlier, latest],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([latest]);
    }

    // An inception-to-date span is longer than any supported duration, so it cannot anchor a
    // bucket that has no conforming span of its own.
    [Fact]
    public void AnchorToLatestPeriodEnd_NoConformingSpan_IgnoresAnInceptionToDateSpan()
    {
        var stub = Duration(new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), 10m);
        var inceptionToDate = Duration(new DateOnly(1998, 1, 1), new DateOnly(2025, 9, 30), 20m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [stub, inceptionToDate],
            SecFiscalPeriod.FullYear
        );

        anchored.Should().BeEquivalentTo([stub]);
    }

    private static FinancialFact Dimensional(FinancialFact fact)
    {
        fact.DimensionsKey = "srt:ConsolidationItemsAxis=us-gaap:OperatingSegmentsMember";
        return fact;
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

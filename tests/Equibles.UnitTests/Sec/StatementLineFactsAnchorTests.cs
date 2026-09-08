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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
        );

        anchored.Should().BeEquivalentTo([latest]);
    }

    [Fact]
    public void AnchorToLatestPeriodEnd_NoFacts_ReturnsEmpty()
    {
        StatementLineFacts
            .AnchorToLatestPeriodEnd([], SecFiscalPeriod.FullYear, reportedPeriodEnd: null)
            .Should()
            .BeEmpty();
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
            SecFiscalPeriod.Q1,
            reportedPeriodEnd: null
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
        );

        anchored.Should().BeEquivalentTo([later]);
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
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
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: null
        );

        anchored.Should().BeEquivalentTo([stub]);
    }

    // A span of the right LENGTH can measure another period entirely. Adobe tags a 25-day
    // segment window into its FY2025 Q1 bucket; it conforms, ends latest, and so anchored the
    // quarter on 2025-03-26, rendering ONE line instead of the eleven of the real quarter.
    // The company's own balance sheet for that quarter states 2025-02-28.
    [Fact]
    public void AnchorToLatestPeriodEnd_ReportedPeriodEndWithAConformingSpan_AnchorsThere()
    {
        var quarter = Duration(new DateOnly(2024, 11, 30), new DateOnly(2025, 2, 28), 1_000m);
        var alsoTheQuarter = Duration(new DateOnly(2024, 11, 30), new DateOnly(2025, 2, 28), 500m);
        var segmentWindow = Dimensional(
            Duration(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 26), 7m)
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [quarter, alsoTheQuarter, segmentWindow],
            SecFiscalPeriod.Q1,
            reportedPeriodEnd: new DateOnly(2025, 2, 28)
        );

        anchored.Should().BeEquivalentTo([quarter, alsoTheQuarter]);
    }

    // The floor is a floor, not an equality: the confirmed date may sit ABOVE the latest
    // consolidated span. A filer whose only consolidated span under the stamp is the
    // comparative still gets its real quarter, and not the segment window past it.
    [Fact]
    public void AnchorToLatestPeriodEnd_ReportedPeriodEndAboveTheMeasuredSpan_AnchorsThere()
    {
        var consolidatedComparative = Duration(
            new DateOnly(2024, 9, 1),
            new DateOnly(2024, 11, 29),
            10m
        );
        var theQuarter = Dimensional(
            Duration(new DateOnly(2024, 11, 30), new DateOnly(2025, 2, 28), 1_000m)
        );
        var alsoTheQuarter = Dimensional(
            Duration(new DateOnly(2024, 11, 30), new DateOnly(2025, 2, 28), 500m)
        );
        var segmentWindow = Dimensional(
            Duration(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 26), 7m)
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [consolidatedComparative, theQuarter, alsoTheQuarter, segmentWindow],
            SecFiscalPeriod.Q1,
            reportedPeriodEnd: new DateOnly(2025, 2, 28)
        );

        anchored.Should().BeEquivalentTo([theQuarter, alsoTheQuarter]);
    }

    // The balance-sheet date is evidence, never an override. A filer can file its
    // quarter-end balance sheet under the NEXT fiscal stamp, leaving only the prior year's
    // same-quarter instant under this one (DELL); anchoring on it would replace a complete
    // quarter with the comparative column.
    [Fact]
    public void AnchorToLatestPeriodEnd_ReportedPeriodEndBelowAMeasuredSpan_KeepsTheStatement()
    {
        var priorYearComparative = Duration(
            new DateOnly(2024, 2, 3),
            new DateOnly(2024, 5, 3),
            10m
        );
        var quarter = Duration(new DateOnly(2025, 2, 1), new DateOnly(2025, 5, 2), 1_000m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [priorYearComparative, quarter],
            SecFiscalPeriod.Q1,
            reportedPeriodEnd: new DateOnly(2024, 5, 3)
        );

        anchored.Should().BeEquivalentTo([quarter]);
    }

    // A wrong balance-sheet date must change nothing. HOFT's FY2025 bucket carries a stray
    // 2025-03-31 instant while its year ended 2025-02-02; no span ends there, so the ladder
    // decides exactly as before.
    [Fact]
    public void AnchorToLatestPeriodEnd_ReportedPeriodEndWithNoConformingSpan_FallsBack()
    {
        var trailingTwelveMonths = Duration(
            new DateOnly(2024, 5, 6),
            new DateOnly(2025, 5, 4),
            1_000m
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [trailingTwelveMonths],
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: new DateOnly(2025, 3, 31)
        );

        anchored.Should().BeEquivalentTo([trailingTwelveMonths]);
    }

    // The proof behind the two OSS call sites passing null: over CONSOLIDATED facts the
    // latest conforming span already IS the entity's own measured endpoint, so no date can
    // move the anchor — above it nothing conforms, at or below it the floor refuses.
    [Theory]
    [InlineData(2021, 12, 31)]
    [InlineData(2022, 6, 30)]
    [InlineData(2022, 12, 31)]
    [InlineData(2023, 3, 31)]
    public void AnchorToLatestPeriodEnd_ConsolidatedFacts_AreUnmovedByAnyReportedEnd(
        int year,
        int month,
        int day
    )
    {
        var comparative = Duration(new DateOnly(2021, 1, 1), new DateOnly(2021, 12, 31), 900m);
        var year2022 = Duration(new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31), 1_000m);
        var closingCash = Instant(new DateOnly(2022, 12, 31), 250m);
        List<FinancialFact> facts = [comparative, year2022, closingCash];

        StatementLineFacts
            .AnchorToLatestPeriodEnd(
                facts,
                SecFiscalPeriod.FullYear,
                reportedPeriodEnd: new DateOnly(year, month, day)
            )
            .Should()
            .BeEquivalentTo(
                StatementLineFacts.AnchorToLatestPeriodEnd(
                    facts,
                    SecFiscalPeriod.FullYear,
                    reportedPeriodEnd: null
                )
            );
    }

    // A balance sheet is every-fact-an-instant, so it has no conforming span to confirm and
    // the rule can never fire — 0 of 268,534 balance-sheet buckets move on prod.
    [Fact]
    public void AnchorToLatestPeriodEnd_AllInstantStatement_IgnoresTheReportedPeriodEnd()
    {
        var comparative = Instant(new DateOnly(2021, 12, 31), 900m);
        var currentAssets = Instant(new DateOnly(2022, 12, 31), 1_000m);

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [comparative, currentAssets],
            SecFiscalPeriod.FullYear,
            reportedPeriodEnd: new DateOnly(2021, 12, 31)
        );

        anchored.Should().BeEquivalentTo([currentAssets]);
    }

    // A period a filer tagged only per-segment has no measured consolidated span to floor
    // against, so a confirmed date still anchors it.
    [Fact]
    public void AnchorToLatestPeriodEnd_DimensionalOnlyAtTheReportedEnd_StillAnchorsThere()
    {
        var segmentQuarter = Dimensional(
            Duration(new DateOnly(2024, 11, 30), new DateOnly(2025, 2, 28), 40m)
        );
        var laterSegmentWindow = Dimensional(
            Duration(new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 26), 7m)
        );

        var anchored = StatementLineFacts.AnchorToLatestPeriodEnd(
            [segmentQuarter, laterSegmentWindow],
            SecFiscalPeriod.Q1,
            reportedPeriodEnd: new DateOnly(2025, 2, 28)
        );

        anchored.Should().BeEquivalentTo([segmentQuarter]);
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

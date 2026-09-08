using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.UnitTests.Sec;

public class HistoricalFiscalCalendarTests
{
    [Theory]
    [InlineData(3, 31, SecFiscalPeriod.Q1)]
    [InlineData(6, 30, SecFiscalPeriod.Q2)]
    [InlineData(9, 30, SecFiscalPeriod.Q3)]
    public void HistoricalQuarterAndInstantUseReportedAnnualCalendar(
        int month,
        int day,
        SecFiscalPeriod expected
    )
    {
        var calendar = new HistoricalFiscalCalendar(
            [(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))],
            [],
            6,
            30
        );
        var end = new DateOnly(2025, month, day);
        calendar.Resolve(end, end).Should().Be((2025, expected));
        calendar.Resolve(new DateOnly(2025, 1, 1), end).Should().Be((2025, expected));
    }

    [Fact]
    public void OpenYearUsesItsOwnFilingCalendarWithoutReplacingCurrentCalendar()
    {
        var end = new DateOnly(2026, 3, 31);
        var calendar = new HistoricalFiscalCalendar(
            [],
            [new ParsedFiscalYearEnd("1274173", new DateOnly(2026, 1, 1), end, 12, 31)],
            6,
            30
        );
        calendar.Resolve(end, end).Should().Be((2026, SecFiscalPeriod.Q1));
        calendar.Resolve(new DateOnly(2026, 1, 1), end).Should().Be((2026, SecFiscalPeriod.Q1));
        calendar
            .Resolve(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30))
            .Should()
            .Be((2027, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void ConflictingSourceCalendarsDoNotChooseOne()
    {
        var end = new DateOnly(2026, 3, 31);
        var calendar = new HistoricalFiscalCalendar(
            [],
            [new("1274173", end, end, 12, 31), new("1274173", end, end, 6, 30)],
            6,
            30
        );
        calendar.Resolve(end, end).Should().BeNull();
    }

    [Fact]
    public void ChangedCalendarAbstainsUntilTheMeasuredPeriodHasEvidence()
    {
        var end = new DateOnly(2026, 3, 31);
        var calendar = new HistoricalFiscalCalendar([], [], 6, 30, true);
        calendar.Resolve(end, end).Should().BeNull();
        calendar.HasEvidence(end, end).Should().BeFalse();
    }

    [Fact]
    public void FingerprintTracksCalendarEvidenceButIgnoresOrderingAndDuplicateContexts()
    {
        var end = new DateOnly(2026, 3, 31);
        var first = new ParsedFiscalYearEnd("1274173", new DateOnly(2026, 1, 1), end, 12, 31);
        var duplicateContext = first with { Cik = "0001274173" };
        var annual = (new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var a = new HistoricalFiscalCalendar([annual], [first], 6, 30, true);
        var b = new HistoricalFiscalCalendar(
            [annual, annual],
            [duplicateContext, first],
            6,
            30,
            true
        );
        a.Fingerprint.Should().Be(b.Fingerprint);
        a.Fingerprint.Should()
            .NotBe(new HistoricalFiscalCalendar([annual], [], 6, 30, true).Fingerprint);
        a.Fingerprint.Should()
            .NotBe(new HistoricalFiscalCalendar([annual], [first], 12, 31, true).Fingerprint);
    }

    [Fact]
    public void OverlappingReportedAnnualCalendarsAbstain()
    {
        var end = new DateOnly(2025, 6, 30);
        var calendar = new HistoricalFiscalCalendar(
            [(new(2025, 1, 1), new(2025, 12, 31)), (new(2024, 7, 1), end)],
            [],
            6,
            30
        );
        calendar.Resolve(end, end).Should().BeNull();
    }

    [Fact]
    public void ShortSourceStampedPeriodIsNotAConflictingCalendar()
    {
        var start = new DateOnly(2025, 2, 1);
        var end = new DateOnly(2025, 3, 31);
        var calendar = new HistoricalFiscalCalendar(
            [(new(2025, 1, 1), new(2025, 12, 31))],
            [],
            6,
            30,
            true
        );
        calendar.Resolve(start, end).Should().BeNull();
        calendar.RefusesCalendar(start, end).Should().BeFalse();
    }

    [Fact]
    public void ContextCalendarAppliesWithinItsStatedWindowAndRefusesLaterDates()
    {
        var end = new DateOnly(2026, 3, 31);
        var calendar = new HistoricalFiscalCalendar(
            [],
            [new("1274173", new(2026, 1, 1), end, 12, 31)],
            6,
            30,
            true
        );
        calendar
            .Resolve(new(2026, 3, 24), new(2026, 3, 24))
            .Should()
            .Be((2026, SecFiscalPeriod.Q1));
        calendar.RefusesCalendar(new(2026, 4, 1), new(2026, 4, 1)).Should().BeTrue();
        calendar.RefusesCalendar(new(2025, 12, 31), new(2026, 3, 24)).Should().BeTrue();
    }
}

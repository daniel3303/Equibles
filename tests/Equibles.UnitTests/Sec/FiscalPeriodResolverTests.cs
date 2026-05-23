using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverTests
{
    // Apple FYE 09/28 — 52/53-week filer, so PeriodEnd actually wobbles
    // 2022-09-24, 2023-09-30, 2024-09-28 across three consecutive annuals.

    [Fact]
    public void Resolve_AppleFy2024Annual_LabelsBoth2024AndFullYear()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 09, 30),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_AppleFy2023AnnualReportedInFy2024TenK_LabelsAs2023()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2022, 09, 25),
            periodEnd: new DateOnly(2023, 09, 30),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2023, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_AppleFy2022AnnualReportedInFy2024TenK_LabelsAs2022()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2021, 09, 26),
            periodEnd: new DateOnly(2022, 09, 24),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2022, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_AppleQ1Fy2024_LabelsAs2024Q1()
    {
        // Q1 of Apple's FY2024 ends ~3 months into the new fiscal year.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 10, 01),
            periodEnd: new DateOnly(2023, 12, 30),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void Resolve_AppleQ2Fy2024_LabelsAs2024Q2()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 12, 31),
            periodEnd: new DateOnly(2024, 03, 30),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.Q2));
    }

    [Fact]
    public void Resolve_AppleQ3Fy2024_LabelsAs2024Q3()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 03, 31),
            periodEnd: new DateOnly(2024, 06, 29),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.Q3));
    }

    [Fact]
    public void Resolve_MicrosoftFy2024Annual_LabelsAs2024FullYear()
    {
        // Microsoft FYE 06/30 — a different month entirely from Apple.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 07, 01),
            periodEnd: new DateOnly(2024, 06, 30),
            fyeMonth: 6,
            fyeDay: 30
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_WalmartFy2024Annual_LabelsAs2024FullYear()
    {
        // Walmart FYE 01/31 — the fiscal year spans most of the prior
        // calendar year, so the labelling rule "year of the FYE date" still
        // produces Walmart's own FY2024.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 02, 01),
            periodEnd: new DateOnly(2024, 01, 31),
            fyeMonth: 1,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_WalmartQ3Fy2024_LabelsAs2024Q3()
    {
        // Walmart's Q3 ends 2023-10-31 (Aug–Oct), the third quarter of FY2024
        // (which closes 2024-01-31).
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 08, 01),
            periodEnd: new DateOnly(2023, 10, 31),
            fyeMonth: 1,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q3));
    }

    [Fact]
    public void Resolve_Q4ReportedSeparately_LabelsAsQ4()
    {
        // Some filers report Q4 as a standalone 3-month period ending at
        // the fiscal year end.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 06, 30),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.Q4));
    }

    [Fact]
    public void Resolve_InstantPointAtFye_LabelsAsFullYearOfThatYear()
    {
        // Balance-sheet facts have start == end (Instant period). They sit
        // at a point in time, conventionally the fiscal year end.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 09, 28),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_LeapYearFebFye_HandlesNonLeapYearWithoutThrowing()
    {
        // A FYE of Feb 29 lands on Feb 28 in non-leap years. The resolver
        // must clamp without throwing and still match the period end.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2022, 03, 01),
            periodEnd: new DateOnly(2023, 02, 28),
            fyeMonth: 2,
            fyeDay: 29
        );

        result.Should().Be((2023, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_FyeMonthNull_ReturnsNullSoCallerFallsBack()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 09, 30),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: null,
            fyeDay: 28
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_FyeDayNull_ReturnsNullSoCallerFallsBack()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 09, 30),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 9,
            fyeDay: null
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_OutOfRangeFyeMonth_ReturnsNull()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 09, 30),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 13,
            fyeDay: 28
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_AnnualPeriodFarFromAnyFye_ReturnsNullForCallerFallback()
    {
        // 12-month period ending nowhere near the company's FYE — surfaces
        // as null so the caller doesn't invent a wrong fiscal-year label.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 03, 01),
            periodEnd: new DateOnly(2024, 03, 01),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_UnknownDurationShape_ReturnsNull()
    {
        // 60-day period is neither a quarter (~90) nor an annual (~365).
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 01, 01),
            periodEnd: new DateOnly(2024, 03, 01),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_SixMonthCumulative_LabelsAsQ2()
    {
        // YTD cumulative through Q2 — 6 months ending at the half-year
        // mark. Conventionally labeled Q2 in MD&A-style tables.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 10, 01),
            periodEnd: new DateOnly(2024, 03, 30),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.Q2));
    }

    [Fact]
    public void Resolve_NineMonthCumulative_LabelsAsQ3()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 10, 01),
            periodEnd: new DateOnly(2024, 06, 29),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().Be((2024, SecFiscalPeriod.Q3));
    }

    [Fact]
    public void Resolve_FyeMonthZero_ReturnsNull()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 01, 01),
            periodEnd: new DateOnly(2023, 12, 31),
            fyeMonth: 0,
            fyeDay: 31
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_FyeDayZero_ReturnsNull()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 01, 01),
            periodEnd: new DateOnly(2023, 12, 31),
            fyeMonth: 12,
            fyeDay: 0
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_AnnualDecemberFye_ReturnsFullYear()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 01, 01),
            periodEnd: new DateOnly(2023, 12, 31),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2023, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_AnnualJuneFye_ReturnsFullYear()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 07, 01),
            periodEnd: new DateOnly(2024, 06, 30),
            fyeMonth: 6,
            fyeDay: 30
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_52WeekFiler_MatchesWithinWindow()
    {
        // Apple-style: nominal FYE 09/30 but actual period ends 09/28.
        // The 2-day drift is within FyeMatchWindowDays (14).
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 10, 01),
            periodEnd: new DateOnly(2024, 09, 28),
            fyeMonth: 9,
            fyeDay: 30
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_InstantAtDecemberFye_ReturnsFullYear()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 12, 31),
            periodEnd: new DateOnly(2024, 12, 31),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_Q1_DecemberFye()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 01, 01),
            periodEnd: new DateOnly(2024, 03, 31),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void Resolve_Q2_DecemberFye_HalfYear()
    {
        // YTD cumulative through Q2 — 182 days (within 170-190 half-year range).
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 01, 01),
            periodEnd: new DateOnly(2024, 06, 30),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q2));
    }

    [Fact]
    public void Resolve_Q3_DecemberFye_NineMonth()
    {
        // YTD cumulative through Q3 — 274 days (within 260-280 nine-month range).
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 01, 01),
            periodEnd: new DateOnly(2024, 09, 30),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q3));
    }

    [Fact]
    public void Resolve_Q1_MarchFye()
    {
        // FYE March 31. Fiscal year Apr 2024 - Mar 2025.
        // Q1 = Apr-Jun 2024. endingFye = 2025-03-31, year = 2025.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 04, 01),
            periodEnd: new DateOnly(2024, 06, 30),
            fyeMonth: 3,
            fyeDay: 31
        );

        result.Should().Be((2025, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void Resolve_Q2_JuneFye_HalfYear()
    {
        // FYE June 30. Fiscal year Jul 2024 - Jun 2025.
        // Half-year cumulative = Jul-Dec 2024.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 07, 01),
            periodEnd: new DateOnly(2024, 12, 31),
            fyeMonth: 6,
            fyeDay: 30
        );

        result.Should().Be((2025, SecFiscalPeriod.Q2));
    }

    [Fact]
    public void Resolve_200DayDuration_ReturnsNull()
    {
        // 200 days falls between half-year (170-190) and nine-month (260-280) ranges.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 01, 01),
            periodEnd: new DateOnly(2024, 07, 19),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_AnnualButFyeTooFar_ReturnsNull()
    {
        // Annual-length period (365 days) but FYE is far from periodEnd.
        // Period ends 2024-06-15 with FYE 12/31 — closest FYE candidate is
        // Dec 31, which is ~6 months away, well beyond the 14-day window.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 06, 16),
            periodEnd: new DateOnly(2024, 06, 15),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_Feb29Fye_NonLeapYear_ClampsToFeb28()
    {
        // FYE Feb 29 in a non-leap year: CreateSafe clamps to Feb 28.
        // Annual period ending Feb 28, 2023 should resolve because the
        // clamped FYE (Feb 28) is within the 14-day window.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2022, 03, 01),
            periodEnd: new DateOnly(2023, 02, 28),
            fyeMonth: 2,
            fyeDay: 29
        );

        result.Should().Be((2023, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_PeriodEndYear1_AnnualDoesNotThrow()
    {
        // Year=1: CreateSafe(0,...) returns DateOnly.MinValue. Annual path
        // uses ClosestTo which handles MinValue fine, no AddYears call.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(1, 1, 1),
            periodEnd: new DateOnly(1, 12, 31),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((1, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_PeriodEndYear1_QuarterlyThrows()
    {
        // Quarterly path calls endingFye.AddYears(-1) which underflows when
        // endingFye is year 1. This documents the current limitation.
        var periodStart = new DateOnly(1, 1, 1);
        var periodEnd = new DateOnly(1, 3, 31);

        var act = () => FiscalPeriodResolver.Resolve(periodStart, periodEnd, 12, 31);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

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
}

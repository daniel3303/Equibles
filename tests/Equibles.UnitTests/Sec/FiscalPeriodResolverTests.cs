using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FiscalPeriodResolverTests
{
    [Fact]
    public void Resolve_NullFyeMonth_ReturnsNull()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 1, 1),
            periodEnd: new DateOnly(2023, 12, 31),
            fyeMonth: null,
            fyeDay: 31
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NullFyeDay_ReturnsNull()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 1, 1),
            periodEnd: new DateOnly(2023, 12, 31),
            fyeMonth: 12,
            fyeDay: null
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_FyeMonthZero_ReturnsNull()
    {
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 1, 1),
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
            periodStart: new DateOnly(2023, 1, 1),
            periodEnd: new DateOnly(2023, 12, 31),
            fyeMonth: 12,
            fyeDay: 0
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_AnnualDecemberFye_ReturnsFullYear()
    {
        // Calendar-year filer: Jan 1 to Dec 31, fye 12/31
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 1, 1),
            periodEnd: new DateOnly(2023, 12, 31),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2023, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_AnnualJuneFye_ReturnsFullYear()
    {
        // Microsoft-style: Jul 1 to Jun 30, fye 06/30
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 7, 1),
            periodEnd: new DateOnly(2024, 6, 30),
            fyeMonth: 6,
            fyeDay: 30
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_52WeekFiler_MatchesWithinWindow()
    {
        // Apple-style 52-week filer: periodEnd 2024-09-28, fye 09/30.
        // The 2-day gap is within the 14-day window.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 9, 30),
            periodEnd: new DateOnly(2024, 9, 28),
            fyeMonth: 9,
            fyeDay: 30
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_InstantAtDecemberFye_ReturnsFullYear()
    {
        // Balance-sheet instant where periodStart == periodEnd at the FYE date
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
        // Q1 for a Dec FYE filer: Jan 1 to Mar 31 (~90 days)
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 3, 31),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void Resolve_Q2_DecemberFye_HalfYear()
    {
        // YTD cumulative through Q2 for a Dec FYE: Jan 1 to Jun 30 (~182 days)
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 6, 30),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q2));
    }

    [Fact]
    public void Resolve_Q3_DecemberFye_NineMonth()
    {
        // YTD cumulative through Q3 for a Dec FYE: Jan 1 to Sep 30 (~274 days)
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 9, 30),
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().Be((2024, SecFiscalPeriod.Q3));
    }

    [Fact]
    public void Resolve_Q1_MarchFye()
    {
        // Q1 for a March FYE filer: Apr 1 to Jun 30
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 4, 1),
            periodEnd: new DateOnly(2024, 6, 30),
            fyeMonth: 3,
            fyeDay: 31
        );

        result.Should().Be((2025, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void Resolve_200DayDuration_ReturnsNull()
    {
        // 200 days falls between valid ranges (quarterly 80-100, half-year 170-190,
        // nine-month 260-280, annual 350-380) — should return null.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 7, 19), // 200 days
            fyeMonth: 12,
            fyeDay: 31
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_AnnualButFyeTooFar_ReturnsNull()
    {
        // 365-day period but FYE is >14 days from periodEnd
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2023, 3, 1),
            periodEnd: new DateOnly(2024, 3, 1),
            fyeMonth: 9,
            fyeDay: 28
        );

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_Feb29Fye_NonLeapYear_ClampsToFeb28()
    {
        // FYE of Feb 29, non-leap year 2023: CreateSafe clamps to Feb 28.
        // Annual period ending near Feb 28 should match.
        var result = FiscalPeriodResolver.Resolve(
            periodStart: new DateOnly(2022, 3, 1),
            periodEnd: new DateOnly(2023, 2, 28),
            fyeMonth: 2,
            fyeDay: 29
        );

        result.Should().Be((2023, SecFiscalPeriod.FullYear));
    }
}

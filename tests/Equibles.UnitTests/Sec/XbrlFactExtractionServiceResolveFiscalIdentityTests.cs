using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Raw XBRL carries no fy/fp identity, so the extractor derives it. With FYE
/// metadata the shared FiscalPeriodResolver decides; without it (the ~50
/// companies with null FYE, GH-2054) the fallback must still produce a sane
/// identity: annual-length durations map to FY of the period end, anything
/// else to the calendar quarter of the period end — never a fabricated FY
/// label on a three-month period.
/// </summary>
public class XbrlFactExtractionServiceResolveFiscalIdentityTests
{
    [Theory]
    [InlineData(2025, 5, 4, 1, 31, 2026, SecFiscalPeriod.Q1)]
    [InlineData(2025, 8, 3, 1, 31, 2026, SecFiscalPeriod.Q2)]
    [InlineData(2025, 11, 2, 1, 31, 2026, SecFiscalPeriod.Q3)]
    [InlineData(2024, 12, 31, 9, 30, 2025, SecFiscalPeriod.Q1)]
    public void ResolveFiscalIdentity_InterimInstant_UsesReportedFiscalQuarter(
        int year,
        int month,
        int day,
        int fyeMonth,
        int fyeDay,
        int expectedYear,
        SecFiscalPeriod expectedPeriod
    )
    {
        var date = new DateOnly(year, month, day);
        XbrlFactExtractionService
            .ResolveFiscalIdentity(date, date, fyeMonth, fyeDay)
            .Should()
            .Be((expectedYear, expectedPeriod));
    }

    [Fact]
    public void ResolveFiscalIdentity_KnownFye_UsesResolver()
    {
        // Dec-FYE company, Jan–Mar duration → Q1 of fiscal 2025 per the resolver.
        var identity = XbrlFactExtractionService.ResolveFiscalIdentity(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 3, 31),
            fiscalYearEndMonth: 12,
            fiscalYearEndDay: 31
        );

        identity.Should().Be((2025, SecFiscalPeriod.Q1));
    }

    [Fact]
    public void ResolveFiscalIdentity_MissingFyeAnnualDuration_FallsBackToFullYear()
    {
        var identity = XbrlFactExtractionService.ResolveFiscalIdentity(
            new DateOnly(2024, 6, 30),
            new DateOnly(2025, 6, 29),
            fiscalYearEndMonth: null,
            fiscalYearEndDay: null
        );

        identity.Should().Be((2025, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void ResolveFiscalIdentity_MissingFyeQuarterDuration_FallsBackToCalendarQuarter()
    {
        // Quarter ending in May → calendar Q2; must not be stamped FullYear.
        var identity = XbrlFactExtractionService.ResolveFiscalIdentity(
            new DateOnly(2025, 3, 1),
            new DateOnly(2025, 5, 31),
            fiscalYearEndMonth: null,
            fiscalYearEndDay: null
        );

        identity.Should().Be((2025, SecFiscalPeriod.Q2));
    }
}

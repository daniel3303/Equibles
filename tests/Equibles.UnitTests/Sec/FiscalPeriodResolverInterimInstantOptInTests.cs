using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the opt-in interim-instant classification: an fp-less companyfacts value
/// (SEC serves 6-K interim balance sheets with <c>fp = null</c>) has only its date
/// as identity, so <c>classifyInterimInstants: true</c> resolves a mid-year
/// instant to the fiscal quarter containing it. The flag must not disturb the
/// two existing contracts: an at-FYE instant still resolves to FullYear, and
/// default callers (no flag) still get null for a mid-year instant so the
/// SEC-supplied identity keeps winning for fp-carrying values (#982).
/// </summary>
public class FiscalPeriodResolverInterimInstantOptInTests
{
    [Theory]
    [InlineData(3, 31, SecFiscalPeriod.Q1)]
    [InlineData(6, 30, SecFiscalPeriod.Q2)]
    [InlineData(9, 30, SecFiscalPeriod.Q3)]
    public void Resolve_OptedInInterimInstant_ClassifiesByContainingQuarter(
        int month,
        int day,
        SecFiscalPeriod expected
    )
    {
        var instant = new DateOnly(2024, month, day);

        var result = FiscalPeriodResolver.Resolve(
            instant,
            instant,
            12,
            31,
            classifyInterimInstants: true
        );

        result.Should().Be((2024, expected));
    }

    [Fact]
    public void Resolve_OptedInInstantAtFye_StillResolvesFullYear()
    {
        var instant = new DateOnly(2024, 12, 31);

        var result = FiscalPeriodResolver.Resolve(
            instant,
            instant,
            12,
            31,
            classifyInterimInstants: true
        );

        result.Should().Be((2024, SecFiscalPeriod.FullYear));
    }

    [Fact]
    public void Resolve_OptedInUnrecognisedDuration_StillReturnsNull()
    {
        // The opt-in covers instants only — a 130-day span is neither a quarter
        // nor a half-year whatever the caller asked for.
        var end = new DateOnly(2024, 6, 30);

        var result = FiscalPeriodResolver.Resolve(
            end.AddDays(-130),
            end,
            12,
            31,
            classifyInterimInstants: true
        );

        result.Should().BeNull();
    }
}

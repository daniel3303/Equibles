using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

// The FullYear bucket is the enum's zero value, so it collects unclassified periods on
// top of the discrete-Q4-under-fp=FY case. When no ordinary annual-span fact exists in
// the bucket, a sub-annual duration may publish as the year ONLY when the surrounding
// fiscal years seam it in on both sides — the previous annual window ends where it
// starts AND the next annual window starts where it ends. One-sided seams are exactly
// the two wrong shapes: a year-to-date span abuts only on its start (its fiscal year
// began there) and a lone discrete fourth quarter only on its end (the next year
// begins there).
public class AnnualTransitionStubsTests
{
    // A genuine transition stub: the issuer moved its year end from December to June,
    // filing a six-month "year" (Jan-Jun 2024) between the old December-ending year
    // and the new June-ending one.
    [Fact]
    public void IsCorroborated_TransitionStubSeamedOnBothSides_Corroborates()
    {
        var corroborated = AnnualTransitionStubs.IsCorroborated(
            stubStart: new DateOnly(2024, 1, 1),
            stubEnd: new DateOnly(2024, 6, 30),
            durations:
            [
                (new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31)),
                (new DateOnly(2024, 7, 1), new DateOnly(2025, 6, 30)),
            ]
        );

        corroborated.Should().BeTrue("both neighbouring annual windows seam the stub in");
    }

    // A six-month year-to-date span starts where its own fiscal year does (the prior
    // annual window abuts its start), but the NEXT annual window starts half a year
    // after it ends — the missing end-side seam is what refuses it.
    [Fact]
    public void IsCorroborated_YearToDateSpan_IsRefused()
    {
        var corroborated = AnnualTransitionStubs.IsCorroborated(
            stubStart: new DateOnly(2025, 1, 1),
            stubEnd: new DateOnly(2025, 6, 30),
            durations:
            [
                (new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
                (new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
                (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            ]
        );

        corroborated.Should().BeFalse("no annual window starts where the YTD span ends");
    }

    // A lone discrete fourth quarter under fp=FY ends where the year does (the next
    // annual window abuts its end), but no annual window ends where the quarter
    // starts — the missing start-side seam refuses it.
    [Fact]
    public void IsCorroborated_DiscreteFourthQuarter_IsRefused()
    {
        var corroborated = AnnualTransitionStubs.IsCorroborated(
            stubStart: new DateOnly(2025, 10, 1),
            stubEnd: new DateOnly(2025, 12, 31),
            durations:
            [
                (new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
                (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            ]
        );

        corroborated.Should().BeFalse("no annual window ends where the quarter starts");
    }

    // A two-year cumulative duration is seamed on both sides by the years before and
    // after it — the span floor is what refuses it, not the seam test.
    [Fact]
    public void IsCorroborated_MultiYearCumulative_IsRefused()
    {
        var corroborated = AnnualTransitionStubs.IsCorroborated(
            stubStart: new DateOnly(2023, 1, 1),
            stubEnd: new DateOnly(2024, 12, 31),
            durations:
            [
                (new DateOnly(2022, 1, 1), new DateOnly(2022, 12, 31)),
                (new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            ]
        );

        corroborated.Should().BeFalse("a span at or above the annual floor is never a stub");
    }

    // 52/53-week filers drift up to a week against the calendar; the seam tolerates
    // exactly that drift and no more.
    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void IsCorroborated_SeamToleratesCalendarDriftOnly(int driftDays, bool expected)
    {
        var corroborated = AnnualTransitionStubs.IsCorroborated(
            stubStart: new DateOnly(2024, 1, 1),
            stubEnd: new DateOnly(2024, 6, 30),
            durations:
            [
                (new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31).AddDays(-driftDays)),
                (new DateOnly(2024, 7, 1).AddDays(driftDays), new DateOnly(2025, 6, 30)),
            ]
        );

        corroborated.Should().Be(expected);
    }

    // Only ANNUAL-span windows corroborate: quarters abutting the stub on both sides
    // do not seam it into an annual timeline.
    [Fact]
    public void IsCorroborated_QuarterWindowsNeverCorroborate()
    {
        var corroborated = AnnualTransitionStubs.IsCorroborated(
            stubStart: new DateOnly(2024, 1, 1),
            stubEnd: new DateOnly(2024, 6, 30),
            durations:
            [
                (new DateOnly(2023, 10, 1), new DateOnly(2023, 12, 31)),
                (new DateOnly(2024, 7, 1), new DateOnly(2024, 9, 30)),
            ]
        );

        corroborated.Should().BeFalse("only annual-span windows corroborate a stub");
    }

    // The entity overload: a null context corroborates nothing (fail closed), and
    // instants in the context never act as annual windows.
    [Fact]
    public void IsCorroborated_NullContextOrInstantsOnly_FailsClosed()
    {
        var stub = new FinancialFact
        {
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 6, 30),
            PeriodType = FactPeriodType.Duration,
        };
        var instant = new FinancialFact
        {
            PeriodStart = new DateOnly(2023, 12, 31),
            PeriodEnd = new DateOnly(2023, 12, 31),
            PeriodType = FactPeriodType.Instant,
        };

        AnnualTransitionStubs.IsCorroborated(stub, null).Should().BeFalse();
        AnnualTransitionStubs.IsCorroborated(stub, [instant]).Should().BeFalse();
    }

    // FinancialFactsTools mixes two constant sets in one code path: the Mcp-internal
    // FiscalPeriodSpanDays gates the preferred annual filter while AnnualTransitionStubs
    // carries its own bounds for the fallback. If they drift, a span one side calls
    // annual stops seaming the other side's stubs.
    [Fact]
    public void AnnualBounds_MatchTheMcpSpanConstants()
    {
        AnnualTransitionStubs
            .MinAnnualSpanDays.Should()
            .Be(Equibles.Sec.FinancialFacts.Mcp.Helpers.FiscalPeriodSpanDays.MinAnnualSpanDays);
        AnnualTransitionStubs
            .MaxAnnualSpanDays.Should()
            .Be(Equibles.Sec.FinancialFacts.Mcp.Helpers.FiscalPeriodSpanDays.MaxAnnualSpanDays);
    }
}

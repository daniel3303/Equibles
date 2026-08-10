using System.Reflection;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

// Pins the date guard on GetLatestPrices' Change / Change % columns.
//
// The defect this defends against: the tool takes the two newest DailyStockPrice rows and
// differences them. The end-of-day lane crawls the whole common-stock universe and can finish
// a session or more behind, so the second-newest row is frequently NOT the previous trading
// session — and the difference is then a multi-session move presented as a one-day change.
// Measured in production: SWDR's two newest rows sat 25 sessions apart and the tool reported
// +11,630.77% as its day change.
//
// The guard is by DATE, not by row position or by a calendar-day tolerance. A tolerance cannot
// work: Mon 2026-07-27 vs Thu 2026-07-23 (Friday skipped, a real reported case) is four
// calendar days apart, and so is Mon 2026-07-06 vs Thu 2026-07-02, which IS adjacent because
// Jul 3 was an observed market close. Only the trading calendar separates those two.
public class StockPriceToolsDayChangeBasisTests
{
    private static MethodInfo Method() =>
        typeof(StockPriceTools).GetMethod(
            "DayChangeBasis",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static decimal? Basis(
        DateOnly latestDate,
        DateOnly? previousDate,
        decimal previousClose = 100m,
        DateOnly? splitBoundaryDate = null
    )
    {
        var latest = new DailyStockPrice { Date = latestDate, Close = 110m };
        var previous =
            previousDate == null
                ? null
                : new DailyStockPrice { Date = previousDate.Value, Close = previousClose };
        return (decimal?)Method().Invoke(null, [latest, previous, splitBoundaryDate]);
    }

    [Fact]
    public void ExposesThePrivateStaticHelper()
    {
        // Guards the reflection lookup itself: a rename would otherwise NRE rather than
        // reporting that the pinned helper is gone.
        Method().Should().NotBeNull();
    }

    [Theory]
    [InlineData(2025, 3, 12, 2025, 3, 11)] // Wed measured from Tue
    [InlineData(2025, 3, 17, 2025, 3, 14)] // Mon measured from Fri
    [InlineData(2025, 1, 21, 2025, 1, 17)] // Tue after MLK Monday — 4 calendar days, adjacent
    [InlineData(2026, 7, 6, 2026, 7, 2)] // Mon after the observed Jul 3 close — also 4 days
    [InlineData(2024, 4, 1, 2024, 3, 28)] // Easter Monday, over Good Friday
    public void AdjacentSessions_YieldTheBasis(
        int year,
        int month,
        int day,
        int prevYear,
        int prevMonth,
        int prevDay
    )
    {
        Basis(new DateOnly(year, month, day), new DateOnly(prevYear, prevMonth, prevDay))
            .Should()
            .Be(100m);
    }

    [Fact]
    public void SkippedSession_YieldsNoBasis()
    {
        // The ADBE case: Friday missing, so Monday's row sits above Thursday's. Four calendar
        // days apart — indistinguishable from the MLK case above without the calendar.
        Basis(new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 23)).Should().BeNull();
    }

    [Fact]
    public void ManySkippedSessions_YieldNoBasis()
    {
        // The SWDR case, the one that produced a five-figure percentage.
        Basis(new DateOnly(2026, 2, 6), new DateOnly(2025, 12, 30)).Should().BeNull();
    }

    [Fact]
    public void SinglePriceRow_YieldsNoBasis()
    {
        Basis(new DateOnly(2025, 3, 12), null).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositivePreviousClose_YieldsNoBasis(decimal previousClose)
    {
        // A zero or negative close cannot be a denominator; the old code guarded this and the
        // guard must survive the date check being added in front of it.
        Basis(new DateOnly(2025, 3, 12), new DateOnly(2025, 3, 11), previousClose)
            .Should()
            .BeNull();
    }

    [Fact]
    public void PreviousRowDatedAfterTheLatest_YieldsNoBasis()
    {
        // Defensive: the caller orders by date descending, but a basis must never come from a
        // row that is not strictly the prior session.
        Basis(new DateOnly(2025, 3, 12), new DateOnly(2025, 3, 13)).Should().BeNull();
    }

    [Fact]
    public void PreviousRowSameDateAsTheLatest_YieldsNoBasis()
    {
        // A duplicate bar is not a prior session; differencing it would state 0.00% as a day
        // change that was never measured.
        Basis(new DateOnly(2025, 3, 12), new DateOnly(2025, 3, 12)).Should().BeNull();
    }

    [Fact]
    public void PriorSessionBeforeSplitBoundary_YieldsNoBasis()
    {
        var splitDate = new DateOnly(2026, 8, 4);

        Basis(splitDate, new DateOnly(2026, 8, 3), splitBoundaryDate: splitDate).Should().BeNull();
    }

    [Fact]
    public void PriorSessionOnSplitBoundary_RemainsComparable()
    {
        var splitDate = new DateOnly(2026, 8, 4);

        Basis(new DateOnly(2026, 8, 5), splitDate, splitBoundaryDate: splitDate).Should().Be(100m);
    }

    [Theory]
    // Bars dated on days the NYSE is shut. Every date strictly between the previous NYSE session
    // and the latest one is a weekend or a holiday, so a bar there belongs to a security trading
    // on another calendar — foreign ordinaries quoted here keep trading through these closures,
    // and production carries 121-297 such bars per holiday. No NYSE session is skipped, so the
    // move is a genuine one-session change and must render.
    [InlineData(2026, 7, 6, 2026, 7, 3)] // observed Independence Day (Jul 4 fell on a Saturday)
    [InlineData(2026, 6, 22, 2026, 6, 19)] // Juneteenth, a Friday in 2026
    [InlineData(2026, 5, 26, 2026, 5, 25)] // Memorial Day
    [InlineData(2026, 4, 6, 2026, 4, 3)] // Good Friday
    public void PriorRowOnAnNyseClosure_StillYieldsTheBasis(
        int year,
        int month,
        int day,
        int prevYear,
        int prevMonth,
        int prevDay
    )
    {
        Basis(new DateOnly(year, month, day), new DateOnly(prevYear, prevMonth, prevDay))
            .Should()
            .Be(100m);
    }

    [Fact]
    public void PriorRowOneSessionBeforeAClosure_StillYieldsTheBasis()
    {
        // The security has no bar on the closure itself, so the pre-closure session IS its prior
        // one. Both this and the case above must pass: the rule is "no NYSE session was skipped",
        // not "the prior row is exactly the NYSE previous session".
        Basis(new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 2)).Should().Be(100m);
    }
}

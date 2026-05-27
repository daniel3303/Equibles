using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsActivityControllerResolveCombinedDateSelectionSingleDateTests
{
    // ResolveCombinedDateSelection has TWO disjoint return paths gated
    // on `combined && isCombinedAvailable`. The combined-view arm
    // unconditionally indexes `reportDates[1]`:
    //     return (true, true, reportDates[0], reportDates[1]);
    // so a maintainer who "simplifies" the isCombinedAvailable
    // expression by dropping the `reportDates.Count >= 2` guard
    // (e.g. under the belief that "the page only renders when we
    // have history") would crash with IndexOutOfRangeException on
    // every fresh-deploy request where only one quarter has loaded.
    // The combined toggle is an idempotent UI control — a user
    // clicks it before the second quarter exists in the database
    // and the page would 500 instead of degrading to the
    // single-quarter view.
    //
    // The CombinedQuarterHelper.IsFilingWindowOpen check inside the
    // `&&` is the SECOND half of the guard, the time-sensitive half.
    // This pin attacks the FIRST half — the count-based guard —
    // which is independent of `DateTime.UtcNow` and therefore
    // deterministically reproducible in unit tests. (The
    // filing-window-open half is pinned in the CombinedQuarterHelper
    // sibling tests; intersection with this controller is not
    // separately testable without mocking time.)
    //
    // Contract: with a single report date AND combined=true:
    //   • isCombinedAvailable must short-circuit to false (Count < 2)
    //   • the combined-view arm must NOT fire (would IOOR on
    //     reportDates[1])
    //   • the fallback arm returns (false, false, singleDate, null)
    //     — surfacing isCombinedAvailable=false so the UI hides the
    //     combined toggle, and previous=null so the UI knows there
    //     is no prior quarter to compare against
    //
    // The 4-tuple assertion (each field separately) catches:
    //   • Dropped Count>=2 guard → IOOR (the reflection invocation
    //     bubbles the inner exception out of Invoke as
    //     TargetInvocationException).
    //   • Inverted IsCombinedAvailable flag → returns (true, false,
    //     ...) — visible because the value-position assertion
    //     fails on Item1.
    //   • Forgotten "is selected" reset → returns (false, true,
    //     ...) — visible because Item2 should be false (the user
    //     can't be SELECTED on combined when it isn't AVAILABLE).
    //   • Wrong previous-date default → returns (..., someDate)
    //     instead of (..., null) — visible because Item4 must be
    //     null when there's no prior date in the list.
    [Fact]
    public void ResolveCombinedDateSelection_SingleReportDateWithCombinedRequested_DegradesToNonCombinedView()
    {
        var method = typeof(HoldingsActivityController).GetMethod(
            "ResolveCombinedDateSelection",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var singleDate = new DateOnly(2025, 12, 31);
        var reportDates = new List<DateOnly> { singleDate };

        var result = ((bool, bool, DateOnly, DateOnly?))
            method!.Invoke(null, [(DateOnly?)null, true, reportDates])!;

        result.Item1.Should().BeFalse("Count < 2 must short-circuit isCombinedAvailable to false");
        result.Item2.Should().BeFalse("cannot be IsCombinedSelected when not IsCombinedAvailable");
        result.Item3.Should().Be(singleDate);
        result.Item4.Should().BeNull("no prior quarter exists with a single-date list");
    }
}

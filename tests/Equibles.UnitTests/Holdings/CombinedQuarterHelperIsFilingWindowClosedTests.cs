using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class CombinedQuarterHelperIsFilingWindowClosedTests
{
    // Sibling to CombinedQuarterHelperIsFilingWindowOpenTests (#2410).
    // The "open-arm" pin asserts a recent quarter returns true; this
    // pin completes the two-arm coverage by asserting an OLD quarter
    // (45+ days past the report date) returns false.
    //
    // Contract (CombinedQuarterHelper.IsFilingWindowOpen):
    //   today <= latestReportDate.AddDays(45)
    // Closed-arm contract:
    //   latestReportDate is far enough in the past that
    //   latestReportDate + 45 < today → return false.
    //
    // The risks this pin uniquely catches and that the open-arm
    // sibling cannot:
    //
    //   • "Return true always" regression — a refactor that
    //     hardcoded the return as `true` (or that inverted the
    //     comparison direction) compiles cleanly, passes the
    //     open-arm sibling (still returns true for recent dates),
    //     and silently keeps the Combined Quarter view visible
    //     FOREVER. The Combined view shows quarter N + (N-1) merged
    //     to mask under-reporting during the 45-day SEC filing
    //     deadline. If it never expires, OLD quarters (e.g. 2-year-
    //     old data) would show as "combined" even though all filers
    //     have long since reported — visually confusing operators
    //     who expect single-quarter accuracy after deadline.
    //
    //   • `<=` → `>=` (operand-direction swap) — the open-arm test
    //     uses today + 1 day so `today <= today+46` is true even
    //     under the swap (the swap makes it `today >= today+46`
    //     which is false, so open-arm would BREAK). But what about
    //     `<=` → `>`? Open-arm tests `today > today+46` → false,
    //     which would make the open-arm assertion FAIL. So actually
    //     the open-arm sibling does catch most comparison swaps.
    //     The unique risk this closed-arm pin catches:
    //
    //   • Hardcoded-true regression (constant return value) —
    //     unreachable from open-arm pin because open-arm expects
    //     true and would still pass.
    //
    //   • `AddDays(45)` → `AddDays(450)` or `AddDays(4500)` — a
    //     fat-finger that adds an order of magnitude to the
    //     deadline. The open-arm pin passes (today+1 is still
    //     within 451 days), but the closed-arm pin fails (a
    //     61-day-old report date IS within 451 days, returns true
    //     when it should be false).
    //
    //   • `AddDays(45)` → `AddDays(-45)` or `AddDays(0)` — sign
    //     flip or zero. The open-arm pin (today+1) would still
    //     pass for AddDays(0) (today <= today+1+0 → true) but FAIL
    //     for AddDays(-45) (today <= today+1-45 → false). The
    //     closed-arm pin catches AddDays(0) which the open-arm
    //     misses.
    //
    // Pin: feed a `latestReportDate` that's 60 days in the past so
    // `today > today-60+45 = today-15` makes the result false.
    // The 60-day choice gives margin against clock drift; even at
    // 46 days the assertion would still hold, but 60 is safer.
    [Fact]
    public void IsFilingWindowOpen_OldQuarterPastDeadline_ReturnsFalse()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latestReportDate = today.AddDays(-60);

        var result = CombinedQuarterHelper.IsFilingWindowOpen(latestReportDate);

        result.Should().BeFalse();
    }
}

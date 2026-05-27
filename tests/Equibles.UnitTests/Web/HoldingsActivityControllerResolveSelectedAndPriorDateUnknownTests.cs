using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class HoldingsActivityControllerResolveSelectedAndPriorDateUnknownTests
{
    // Sibling to HoldingsActivityControllerResolveSelectedAndPriorDateOldestTests
    // (oldest-date → previous null). This pin covers the structurally distinct
    // FALLBACK arm:
    //   var requestedIndex = requested.HasValue ? reportDates.IndexOf(...) : -1;
    //   var selectedIndex = requestedIndex < 0 ? 0 : requestedIndex;
    //   ...
    // When the requested date isn't present in reportDates (IndexOf → -1) OR
    // the user supplies no date at all, selectedIndex defaults to 0 — the
    // NEWEST report. Previous becomes the second-newest.
    //
    // The risk this pin uniquely catches:
    //   • Drop-the-fallback regression — `var selectedIndex = requestedIndex`
    //     (drops the `< 0 ? 0 :` clause) would let -1 propagate into
    //     `reportDates[-1]`, throwing ArgumentOutOfRangeException. Real
    //     production scenario: a user clicks a stale bookmark with a
    //     date that's since been superseded (the dashboard's "filing
    //     date" dropdown auto-fills the freshest two, but old shared
    //     links retain old quarter dates). Every such stale-link
    //     request would 500 instead of degrading gracefully to "show
    //     the newest available quarter".
    //   • Inversion regression — `requestedIndex < 0 ? requestedIndex : 0`
    //     (a flip from a careless ternary edit) would always return -1
    //     for valid requests (throws) and 0 for invalid (silently
    //     degrades). Caught by both pins: this one catches the invalid-
    //     request half passing through to the working fallback, the
    //     oldest sibling catches the valid-request half.
    //   • Default-target regression — `requestedIndex < 0 ? reportDates.Count
    //     - 1 : requestedIndex` (someone "fixes" the default to point at
    //     the OLDEST instead of the NEWEST) would compile, pass the
    //     oldest sibling (its input is valid and resolved to its own
    //     index), and silently shift the default landing date from
    //     "freshest quarter" to "oldest quarter" on every stale-link
    //     visit — the dashboard's first paint would show year-old data
    //     by default.
    //
    // The oldest sibling can't catch any of these — its input is VALID
    // (present in reportDates), so the `< 0` arm never fires there. Only
    // an unknown-date input exercises the fallback path. The pair (oldest
    // + unknown) defends both branches of the `< 0 ? 0 : requestedIndex`
    // ternary.
    //
    // Construction: a three-date list and a request that's NOT in the
    // list. Assert selected = newest (index 0), previous = second-newest
    // (index 1). Both elements distinguish:
    //   • Working fallback: returns (newest, second-newest).
    //   • Dropped fallback: throws (caught by reflection invoke).
    //   • Default-to-oldest: returns (oldest, null) — fails on selected.
    [Fact]
    public void ResolveSelectedAndPriorDate_RequestedNotInReportDates_FallsBackToNewestAsSelected()
    {
        var method = typeof(HoldingsActivityController).GetMethod(
            "ResolveSelectedAndPriorDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var reportDates = new List<DateOnly>
        {
            new(2025, 3, 31),
            new(2024, 12, 31),
            new(2024, 9, 30),
        };
        DateOnly? requested = new(2020, 1, 1); // not in the list

        var result =
            (ValueTuple<DateOnly, DateOnly?>)method!.Invoke(null, [requested, reportDates]);

        result.Item1.Should().Be(new DateOnly(2025, 3, 31));
        result.Item2.Should().Be(new DateOnly(2024, 12, 31));
    }
}

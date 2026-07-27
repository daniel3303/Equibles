using System.Reflection;
using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Tests for the recent-gap heal rule in <see cref="YahooPriceImportService"/>, exercised through
/// the pure private static <c>FindEarliestGap</c> via reflection.
///
/// The sync start date is forward-only (last stored + 1), which is what keeps cycles cheap: an
/// up-to-date stock costs zero Yahoo calls. The cost is that any session the upstream feed failed to
/// serve is lost permanently once a LATER bar lands and drags the start date past it. That is not
/// hypothetical — on 2026-07-24 Yahoo served that whole session's daily bars with null OHLC, the
/// importer correctly refused to store them, and ~5,484 stocks were one settled Monday bar away from
/// keeping a permanent one-session hole.
///
/// Widening the window for a stock that actually has a hole costs nothing extra: it is the same
/// single chart request, and PersistPrices is insert-only so already-stored bars are discarded.
/// </summary>
public class YahooPriceImportServiceGapHealTests
{
    private static readonly MethodInfo FindEarliestGapMethod =
        typeof(YahooPriceImportService).GetMethod(
            "FindEarliestGap",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // 2026-07-27 is a Monday; 07-24 Friday, 07-23 Thursday, 07-25/26 the weekend.
    private static readonly DateOnly Today = new(2026, 7, 27);
    private static readonly DateOnly WindowStart = new(2026, 7, 17);

    // hasHistoryBeforeWindow = true models the common case: an established stock whose bars
    // predate the window, so a missing session anywhere in it — including the leading edge — is a
    // real hole. The new-listing case passes false explicitly.
    private static DateOnly? FindGap(params DateOnly[] storedDates) =>
        (DateOnly?)
            FindEarliestGapMethod.Invoke(null, [storedDates.ToList(), WindowStart, Today, true]);

    private static DateOnly? FindGapForNewListing(params DateOnly[] storedDates) =>
        (DateOnly?)
            FindEarliestGapMethod.Invoke(null, [storedDates.ToList(), WindowStart, Today, false]);

    private static DateOnly D(int day) => new(2026, 7, day);

    [Fact]
    public void AMissingSettledSession_IsReportedAsAGap()
    {
        // The production case: everything through Thursday is stored, Friday never arrived.
        FindGap(D(17), D(20), D(21), D(22), D(23)).Should().Be(D(24));
    }

    [Fact]
    public void ACompleteWindow_ReportsNoGap()
    {
        // Nothing to heal, so the forward-only start date stands and the stock keeps costing
        // zero Yahoo calls — the property the cheap-cycle design depends on.
        FindGap(D(17), D(20), D(21), D(22), D(23), D(24)).Should().BeNull();
    }

    [Fact]
    public void WeekendsAndHolidays_AreNotGaps()
    {
        // 07-25 and 07-26 are a weekend; a calendar-day scan would report them as missing and
        // re-request them on every single cycle, forever.
        FindGap(D(17), D(20), D(21), D(22), D(23), D(24)).Should().BeNull();
    }

    [Fact]
    public void TodaysSessionIsNotAGap_BecauseItHasNotSettled()
    {
        // Today's bar is deliberately never stored (IsSettledDailyBar), so treating it as missing
        // would make every stock look holed every day.
        FindGap(D(17), D(20), D(21), D(22), D(23), D(24)).Should().BeNull();
    }

    [Fact]
    public void ANewListing_IsNotHoledForTheDaysBeforeItsFirstBar()
    {
        // A stock whose history starts inside the window has no bars before its listing date; those
        // are not holes, and scanning from windowStart would re-request them every cycle.
        FindGapForNewListing(D(23), D(24)).Should().BeNull();
    }

    [Fact]
    public void AnEstablishedStock_MissingTheWindowsLeadingEdge_IsAGap()
    {
        // The opposite of the new-listing case, and indistinguishable from it by the in-window
        // dates alone: bars stored for every session EXCEPT the window's first. For a stock with
        // history before the window that first session is a real hole — scanning from the earliest
        // in-window bar would silently shorten the heal window as an outage day slid toward its
        // edge, making the 10-day promise quietly non-uniform.
        FindGap(D(20), D(21), D(22), D(23), D(24)).Should().Be(D(17));
    }

    [Fact]
    public void TheEarliestGapWins_SoOneFetchCoversAllOfThem()
    {
        // The window is re-requested from the earliest hole, so a single widened chart call heals
        // every later hole in the same response.
        FindGap(D(17), D(22), D(23)).Should().Be(D(20));
    }

    [Fact]
    public void NothingStoredInTheWindow_IsPlainStalenessNotAGap()
    {
        // A stock with no recent bars at all is simply behind; the forward-only start date already
        // covers it, and claiming a gap here would widen the window for every dormant stock.
        FindGap().Should().BeNull();
    }
}

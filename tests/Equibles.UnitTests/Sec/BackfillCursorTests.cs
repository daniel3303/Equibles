using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class BackfillCursorTests
{
    private static readonly DateTime Now = new(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Floor_NewCursor_IsNull()
    {
        var cursor = new BackfillCursor("test");

        cursor.Floor.Should().BeNull();
        cursor.IsHydrated.Should().BeFalse();
        cursor.LastFullRescanAt.Should().BeNull();
    }

    [Fact]
    public void Advance_SetsFloorToBatchFrontier()
    {
        var cursor = new BackfillCursor("test");
        var frontier = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

        cursor.Advance(frontier);

        cursor.Floor.Should().Be(frontier);
    }

    [Fact]
    public void Hydrate_SeedsFloorAndFullRescanStamp()
    {
        var cursor = new BackfillCursor("test");
        var floor = Now.AddDays(-2);
        var lastFullRescan = Now.AddHours(-3);

        cursor.Hydrate(floor, lastFullRescan);

        cursor.IsHydrated.Should().BeTrue();
        cursor.Floor.Should().Be(floor);
        cursor.LastFullRescanAt.Should().Be(lastFullRescan);
    }

    [Fact]
    public void Hydrate_SecondCall_IsIgnored()
    {
        // The manager hydrates on first use per process; a later stale re-read must never
        // regress live cursor state (an advanced floor, a fresh rescan stamp).
        var cursor = new BackfillCursor("test");
        cursor.Hydrate(Now.AddDays(-2), Now.AddHours(-3));
        cursor.Advance(Now.AddDays(-1));

        cursor.Hydrate(Now.AddDays(-30), null);

        cursor.Floor.Should().Be(Now.AddDays(-1));
        cursor.LastFullRescanAt.Should().Be(Now.AddHours(-3));
    }

    [Fact]
    public void TryStartFullRescan_NoStamp_AllowsAndKeepsFloor()
    {
        // The frontier must survive a rescan: an empty full scan that discarded it would leave
        // rows arriving right afterwards invisible until the next rescan window. A null stamp
        // (fresh install, no BackfillState row yet) admits the scan immediately.
        var cursor = new BackfillCursor("test");
        var frontier = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        cursor.Advance(frontier);

        var allowed = cursor.TryStartFullRescan(Now);

        allowed.Should().BeTrue();
        cursor.Floor.Should().Be(frontier);
        cursor.LastFullRescanAt.Should().Be(Now);
    }

    [Fact]
    public void TryStartFullRescan_WithinRescanInterval_IsRateLimitedAndKeepsFloor()
    {
        var cursor = new BackfillCursor("test");
        cursor.TryStartFullRescan(Now);
        var frontier = Now.AddMinutes(30);
        cursor.Advance(frontier);

        var allowed = cursor.TryStartFullRescan(Now.AddHours(23));

        allowed.Should().BeFalse();
        cursor.Floor.Should().Be(frontier);
    }

    [Fact]
    public void TryStartFullRescan_AfterRescanInterval_AllowsAgain()
    {
        var cursor = new BackfillCursor("test");
        cursor.TryStartFullRescan(Now);

        var allowed = cursor.TryStartFullRescan(Now.AddHours(25));

        allowed.Should().BeTrue();
    }

    [Fact]
    public void TryStartFullRescan_HydratedRecentStamp_IsRateLimited()
    {
        // The stamp survives restarts via BackfillState precisely so a deploy burst cannot
        // re-run the minutes-long corpus scan on every boot.
        var cursor = new BackfillCursor("test");
        cursor.Hydrate(null, Now.AddHours(-2));

        var allowed = cursor.TryStartFullRescan(Now);

        allowed.Should().BeFalse();
    }

    [Fact]
    public void TryStartFullRescan_HydratedExpiredStamp_Allows()
    {
        var cursor = new BackfillCursor("test");
        cursor.Hydrate(null, Now.AddDays(-2));

        var allowed = cursor.TryStartFullRescan(Now);

        allowed.Should().BeTrue();
        cursor.LastFullRescanAt.Should().Be(Now);
    }

    [Fact]
    public void MarkFullRescanFailed_ReadmitsAfterTheShortRetryInterval_NotTheFullDay()
    {
        // A full rescan that was admitted but failed (query timeout, interrupted process)
        // must not cost the whole daily interval: rows behind the bounded window are
        // reachable only by the full rescan, so charging every fault a day starves them
        // indefinitely under a recurring timeout.
        var cursor = new BackfillCursor("test");
        cursor.TryStartFullRescan(Now);

        cursor.MarkFullRescanFailed(Now);

        cursor.TryStartFullRescan(Now.AddMinutes(29)).Should().BeFalse();
        cursor.TryStartFullRescan(Now.AddMinutes(31)).Should().BeTrue();
    }

    [Fact]
    public void MarkFullRescanFailed_KeepsFloor()
    {
        var cursor = new BackfillCursor("test");
        var frontier = Now.AddDays(-1);
        cursor.Advance(frontier);
        cursor.TryStartFullRescan(Now);

        cursor.MarkFullRescanFailed(Now);

        cursor.Floor.Should().Be(frontier);
    }

    [Fact]
    public void TryStartBoundedRescan_NoFloor_IsRefused()
    {
        // A floorless cursor has never processed anything — there is no frontier to look
        // behind, so only the unfloored full scan applies.
        var cursor = new BackfillCursor("test");

        cursor.TryStartBoundedRescan(Now).Should().BeFalse();
    }

    [Fact]
    public void TryStartBoundedRescan_WithFloor_AllowsAndKeepsFloor()
    {
        var cursor = new BackfillCursor("test");
        var frontier = Now.AddDays(-1);
        cursor.Advance(frontier);

        var allowed = cursor.TryStartBoundedRescan(Now);

        allowed.Should().BeTrue();
        cursor.Floor.Should().Be(frontier);
    }

    [Fact]
    public void TryStartBoundedRescan_WithinInterval_IsRateLimited()
    {
        var cursor = new BackfillCursor("test");
        cursor.Advance(Now.AddDays(-1));
        cursor.TryStartBoundedRescan(Now);

        cursor.TryStartBoundedRescan(Now.AddMinutes(59)).Should().BeFalse();
    }

    [Fact]
    public void TryStartBoundedRescan_AfterInterval_AllowsAgain()
    {
        var cursor = new BackfillCursor("test");
        cursor.Advance(Now.AddDays(-1));
        cursor.TryStartBoundedRescan(Now);

        cursor.TryStartBoundedRescan(Now.AddMinutes(61)).Should().BeTrue();
    }

    [Fact]
    public void TryStartBoundedRescan_FirstInProcess_LooksBackTheWholeWindow()
    {
        // A fresh process cannot know which rows the previous one read, so its first rescan
        // covers the full lookback behind the floor.
        var cursor = new BackfillCursor("test");
        var frontier = Now.AddMinutes(-5);
        cursor.Advance(Now.AddMinutes(-30), frontier);

        cursor.TryStartBoundedRescan(Now).Should().BeTrue();

        cursor.BoundedRescanFloor.Should().Be(frontier - BackfillCursor.BoundedRescanLookback);
    }

    [Fact]
    public void TryStartBoundedRescan_AfterFlooredBatches_StartsAtThePreviousRescansFloor()
    {
        // Floored batches read only rows at or above the floor, so after one rescan the next
        // only needs the rows from where that rescan's floor stood, less the commit margin.
        var cursor = new BackfillCursor("test");
        var frontier = Now.AddMinutes(-5);
        cursor.Advance(frontier);
        cursor.TryStartBoundedRescan(Now);
        cursor.Advance(frontier.AddMinutes(1), frontier.AddMinutes(20));
        cursor.Advance(frontier.AddMinutes(20), frontier.AddMinutes(40));

        cursor.TryStartBoundedRescan(Now.AddMinutes(61)).Should().BeTrue();

        // A row stamped up to an hour before it commits can still land behind that floor.
        cursor.BoundedRescanFloor.Should().Be(frontier.AddHours(-1));
    }

    [Fact]
    public void TryStartBoundedRescan_AfterARescanBatch_StartsAtItsOldestRow()
    {
        // A rescan batch reads stragglers behind the floor; any it fails to process again stay
        // there, so the next rescan must reach back to the batch's oldest row.
        var cursor = new BackfillCursor("test");
        var frontier = Now.AddMinutes(-5);
        cursor.Advance(frontier);
        cursor.TryStartBoundedRescan(Now);
        var straggler = frontier.AddHours(-3);
        cursor.Advance(straggler, frontier.AddHours(-2));
        cursor.Advance(frontier.AddHours(-2), frontier.AddMinutes(30));

        cursor.TryStartBoundedRescan(Now.AddMinutes(61)).Should().BeTrue();

        cursor.BoundedRescanFloor.Should().Be(straggler - BackfillCursor.BoundedRescanCommitMargin);
        cursor.Floor.Should().Be(frontier.AddMinutes(30));
    }

    [Fact]
    public void TryStartBoundedRescan_NeverLooksBackFurtherThanTheWindow()
    {
        // A full rescan batch can read rows from years ago; the bounded tier still stops a
        // lookback behind the floor and leaves those rows to the daily full scan.
        var cursor = new BackfillCursor("test");
        var frontier = Now.AddMinutes(-5);
        cursor.Advance(frontier);
        cursor.TryStartBoundedRescan(Now);
        cursor.Advance(Now.AddYears(-3), Now.AddYears(-2));
        cursor.Advance(Now.AddYears(-2), frontier);

        cursor.TryStartBoundedRescan(Now.AddMinutes(61)).Should().BeTrue();

        cursor.BoundedRescanFloor.Should().Be(frontier - BackfillCursor.BoundedRescanLookback);
    }

    [Fact]
    public void TryStartBoundedRescan_RateLimited_KeepsThePendingWindow()
    {
        // A refused start must not consume the oldest row read, or the rescan after it would
        // skip the batches read in between.
        var cursor = new BackfillCursor("test");
        var frontier = Now.AddMinutes(-5);
        cursor.Advance(frontier);
        cursor.TryStartBoundedRescan(Now);
        var straggler = frontier.AddHours(-2);
        cursor.Advance(straggler, frontier);

        cursor.TryStartBoundedRescan(Now.AddMinutes(30)).Should().BeFalse();
        cursor.TryStartBoundedRescan(Now.AddMinutes(61)).Should().BeTrue();

        cursor.BoundedRescanFloor.Should().Be(straggler - BackfillCursor.BoundedRescanCommitMargin);
    }
}

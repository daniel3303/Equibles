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
    public void BoundedRescanLookback_CoversAWeekBehindTheFloor()
    {
        // LoadBatch floors the bounded rescan at Floor − lookback; a week comfortably covers
        // the realistic stragglers (read-skew commits, partially processed batches) while
        // keeping the rescan an index-range query instead of a corpus scan.
        BackfillCursor.BoundedRescanLookback.Should().Be(TimeSpan.FromDays(7));
    }
}

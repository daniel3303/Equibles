using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class BackfillCursorTests
{
    [Fact]
    public void Floor_NewCursor_IsNull()
    {
        var cursor = new BackfillCursor();

        cursor.Floor.Should().BeNull();
    }

    [Fact]
    public void Advance_SetsFloorToBatchFrontier()
    {
        var cursor = new BackfillCursor();
        var frontier = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

        cursor.Advance(frontier);

        cursor.Floor.Should().Be(frontier);
    }

    [Fact]
    public void TryStartFullRescan_FirstCall_AllowsAndKeepsFloor()
    {
        // The frontier must survive a rescan: an empty full scan that discarded it would leave
        // rows arriving right afterwards invisible until the next rescan window.
        var cursor = new BackfillCursor();
        var frontier = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        cursor.Advance(frontier);

        var allowed = cursor.TryStartFullRescan(new DateTime(2026, 7, 3, 13, 0, 0, DateTimeKind.Utc));

        allowed.Should().BeTrue();
        cursor.Floor.Should().Be(frontier);
    }

    [Fact]
    public void TryStartFullRescan_WithinRescanInterval_IsRateLimitedAndKeepsFloor()
    {
        var cursor = new BackfillCursor();
        var firstScan = new DateTime(2026, 7, 3, 13, 0, 0, DateTimeKind.Utc);
        cursor.TryStartFullRescan(firstScan);
        var frontier = new DateTime(2026, 7, 3, 13, 30, 0, DateTimeKind.Utc);
        cursor.Advance(frontier);

        var allowed = cursor.TryStartFullRescan(firstScan.AddMinutes(59));

        allowed.Should().BeFalse();
        cursor.Floor.Should().Be(frontier);
    }

    [Fact]
    public void TryStartFullRescan_AfterRescanInterval_AllowsAgain()
    {
        var cursor = new BackfillCursor();
        var firstScan = new DateTime(2026, 7, 3, 13, 0, 0, DateTimeKind.Utc);
        cursor.TryStartFullRescan(firstScan);
        cursor.Advance(new DateTime(2026, 7, 3, 13, 30, 0, DateTimeKind.Utc));

        var allowed = cursor.TryStartFullRescan(firstScan.AddMinutes(61));

        allowed.Should().BeTrue();
    }
}

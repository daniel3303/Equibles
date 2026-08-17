using Equibles.Congress.HostedService.Services;
using static Equibles.Congress.HostedService.Services.CongressionalFilingLedger;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// The ledger decides which already-ingested filings a cycle re-downloads. A
/// parser that starts extracting new fields raises its version, which demotes
/// older rows to "not yet ingested" so history is re-parsed — but only a capped
/// slice per cycle, so a version bump drip-feeds its backfill rather than
/// re-fetching the whole archive at once.
/// </summary>
public class CongressionalFilingLedgerSelectProcessedTests
{
    private static LedgerEntry Entry(string id, int version, int day = 1) =>
        new(id, new DateOnly(2025, 1, day), version);

    [Fact]
    public void SelectProcessed_NoVersionBump_SkipsEveryRow()
    {
        // A lane that never raises its version behaves exactly as before.
        List<LedgerEntry> records = [Entry("a", 0), Entry("b", 0), Entry("c", 0)];

        var processed = CongressionalFilingLedger.SelectProcessed(
            records,
            parserVersion: 0,
            reprocessLimit: 0
        );

        processed.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void SelectProcessed_RowsAtCurrentVersion_AreSkipped()
    {
        List<LedgerEntry> records = [Entry("a", 1), Entry("b", 1)];

        var processed = CongressionalFilingLedger.SelectProcessed(records, 1, 10);

        processed.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void SelectProcessed_StaleRowsWithinQuota_AreQueuedForReprocessing()
    {
        List<LedgerEntry> records = [Entry("a", 0), Entry("b", 0), Entry("c", 1)];

        var processed = CongressionalFilingLedger.SelectProcessed(records, 1, 10);

        // Only the row already at the current version is skipped; the two stale
        // rows are absent, which is how the clients learn to re-fetch them.
        processed.Should().BeEquivalentTo(["c"]);
    }

    [Fact]
    public void SelectProcessed_MoreStaleRowsThanQuota_DefersTheRemainder()
    {
        List<LedgerEntry> records =
        [
            Entry("oldest", 0, day: 1),
            Entry("middle", 0, day: 2),
            Entry("newest", 0, day: 3),
        ];

        var processed = CongressionalFilingLedger.SelectProcessed(records, 1, reprocessLimit: 1);

        // Oldest filing first: only "oldest" leaves the processed set this
        // cycle, and the other two stay skipped until a later one.
        processed.Should().BeEquivalentTo(["middle", "newest"]);
    }

    [Fact]
    public void SelectProcessed_SameFilingDate_OrdersBySourceIdSoCyclesAdvance()
    {
        List<LedgerEntry> records = [Entry("b", 0), Entry("a", 0), Entry("c", 0)];

        var processed = CongressionalFilingLedger.SelectProcessed(records, 1, reprocessLimit: 1);

        processed.Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public void SelectProcessed_ZeroQuota_LeavesEveryStaleRowDeferred()
    {
        // A version bump shipped without a quota must not silently re-fetch
        // nothing forever OR everything at once — it defers, and the operator
        // sees no re-ingest until a quota is set.
        List<LedgerEntry> records = [Entry("a", 0), Entry("b", 0)];

        var processed = CongressionalFilingLedger.SelectProcessed(records, 1, reprocessLimit: 0);

        processed.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void SelectProcessed_QuotaLargerThanBacklog_QueuesAllOfIt()
    {
        List<LedgerEntry> records = [Entry("a", 0), Entry("b", 0)];

        var processed = CongressionalFilingLedger.SelectProcessed(
            records,
            1,
            reprocessLimit: 1_000
        );

        processed.Should().BeEmpty();
    }
}

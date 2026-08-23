using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins the retirement rules for trade filings: a filing whose parsed rows were not stored is
/// never recorded; an unresolved ticker is stored as a source fact and therefore records.
/// </summary>
public class CongressionalTradeSyncServiceFilterRecordableTests
{
    private static ProcessedFiling Filing(string sourceId, DateOnly filingDate) =>
        new(sourceId, filingDate, ItemCount: 1);

    private static CongressionalTradeSyncService.TradePersistOutcome Outcome(
        IEnumerable<string> unpersisted = null
    ) => new((unpersisted ?? []).ToHashSet());

    [Fact]
    public void FilterRecordable_CleanFiling_IsRecorded()
    {
        var filings = new List<ProcessedFiling> { Filing("A", new DateOnly(2026, 7, 1)) };

        var recordable = CongressionalTradeSyncService.FilterRecordable(filings, Outcome());

        recordable.Should().ContainSingle(f => f.SourceId == "A");
    }

    [Fact]
    public void FilterRecordable_UnpersistedFiling_IsNeverRecorded()
    {
        // Even an old filing stays unrecorded when its rows were not stored.
        var filings = new List<ProcessedFiling> { Filing("A", new DateOnly(2025, 7, 1)) };

        var recordable = CongressionalTradeSyncService.FilterRecordable(
            filings,
            Outcome(unpersisted: ["A"])
        );

        recordable.Should().BeEmpty();
    }

    [Fact]
    public void FilterRecordable_UnlinkedTickerSourceFact_IsRecorded()
    {
        var filings = new List<ProcessedFiling> { Filing("A", new DateOnly(2026, 7, 1)) };

        var recordable = CongressionalTradeSyncService.FilterRecordable(
            filings,
            Outcome()
        );

        recordable.Should().ContainSingle(f => f.SourceId == "A");
    }

    [Fact]
    public void FilterRecordable_MixedBatch_KeepsOnlyRecordableFilings()
    {
        var filings = new List<ProcessedFiling>
        {
            Filing("clean", new DateOnly(2026, 7, 1)),
            Filing("unlinked", new DateOnly(2026, 7, 1)),
            Filing("unpersisted", new DateOnly(2026, 7, 1)),
        };

        var recordable = CongressionalTradeSyncService.FilterRecordable(
            filings,
            Outcome(unpersisted: ["unpersisted"])
        );

        recordable.Select(f => f.SourceId).Should().BeEquivalentTo("clean", "unlinked");
    }
}

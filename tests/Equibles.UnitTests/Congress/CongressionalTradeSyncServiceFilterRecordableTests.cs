using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins the retirement rules for trade filings: a filing whose rows hit the
/// member-not-found guard is never recorded; a filing with an unmatched
/// ticker is only recorded once it has aged past the listing-lag retry
/// cutoff; everything else records immediately.
/// </summary>
public class CongressionalTradeSyncServiceFilterRecordableTests
{
    private static readonly DateOnly Cutoff = new(2026, 6, 21);

    private static ProcessedFiling Filing(string sourceId, DateOnly filingDate) =>
        new(sourceId, filingDate, ItemCount: 1);

    private static CongressionalTradeSyncService.TradePersistOutcome Outcome(
        IEnumerable<string> unmatched = null,
        IEnumerable<string> unpersisted = null
    ) => new((unmatched ?? []).ToHashSet(), (unpersisted ?? []).ToHashSet());

    [Fact]
    public void FilterRecordable_CleanFiling_IsRecorded()
    {
        var filings = new List<ProcessedFiling> { Filing("A", Cutoff.AddDays(10)) };

        var recordable = CongressionalTradeSyncService.FilterRecordable(filings, Outcome(), Cutoff);

        recordable.Should().ContainSingle(f => f.SourceId == "A");
    }

    [Fact]
    public void FilterRecordable_UnpersistedFiling_IsNeverRecorded()
    {
        // Even an old filing stays unrecorded when its rows were not stored.
        var filings = new List<ProcessedFiling> { Filing("A", Cutoff.AddYears(-1)) };

        var recordable = CongressionalTradeSyncService.FilterRecordable(
            filings,
            Outcome(unpersisted: ["A"]),
            Cutoff
        );

        recordable.Should().BeEmpty();
    }

    [Fact]
    public void FilterRecordable_UnmatchedTickerInsideRetryWindow_IsNotRecorded()
    {
        var filings = new List<ProcessedFiling> { Filing("A", Cutoff.AddDays(1)) };

        var recordable = CongressionalTradeSyncService.FilterRecordable(
            filings,
            Outcome(unmatched: ["A"]),
            Cutoff
        );

        recordable.Should().BeEmpty();
    }

    [Fact]
    public void FilterRecordable_UnmatchedTickerPastRetryWindow_IsRetired()
    {
        var filings = new List<ProcessedFiling> { Filing("A", Cutoff) };

        var recordable = CongressionalTradeSyncService.FilterRecordable(
            filings,
            Outcome(unmatched: ["A"]),
            Cutoff
        );

        recordable.Should().ContainSingle(f => f.SourceId == "A");
    }

    [Fact]
    public void FilterRecordable_MixedBatch_KeepsOnlyRecordableFilings()
    {
        var filings = new List<ProcessedFiling>
        {
            Filing("clean", Cutoff.AddDays(5)),
            Filing("unmatched-recent", Cutoff.AddDays(5)),
            Filing("unmatched-old", Cutoff.AddDays(-5)),
            Filing("unpersisted", Cutoff.AddDays(-5)),
        };

        var recordable = CongressionalTradeSyncService.FilterRecordable(
            filings,
            Outcome(unmatched: ["unmatched-recent", "unmatched-old"], unpersisted: ["unpersisted"]),
            Cutoff
        );

        recordable.Select(f => f.SourceId).Should().BeEquivalentTo("clean", "unmatched-old");
    }
}

namespace Equibles.Integrations.DelayedTrades;

// A venue that publishes a delayed post-trade file per location; the lane is venue-pluggable through this seam.
public interface IDelayedTradeSource
{
    string SourceKey { get; }
    DelayedTradeAttribution Attribution { get; }

    Task<DelayedTradeFile> Fetch(
        string locationCode,
        DelayedTradeWindow window,
        CancellationToken cancellationToken
    );

    // Re-enumerable and streaming: every enumeration re-opens the file, and nothing but the current row is held.
    IEnumerable<DelayedTradePrint> Parse(DelayedTradeFile file, DelayedTradeParseCounters counters);
}

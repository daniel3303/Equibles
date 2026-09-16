namespace Equibles.Integrations.DelayedTrades;

// Filled while a file streams, because a lazy parse cannot return totals alongside its rows.
public sealed class DelayedTradeParseCounters
{
    public int Rows { get; set; }
    public int MalformedRows { get; set; }
}

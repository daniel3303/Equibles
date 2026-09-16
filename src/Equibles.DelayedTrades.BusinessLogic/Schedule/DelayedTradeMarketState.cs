namespace Equibles.DelayedTrades.BusinessLogic.Schedule;

// What one process remembers about a market between control ticks; a restart starts from nothing and the
// partition markers make the first settle attempt a cheap no-op.
public sealed class DelayedTradeMarketState
{
    public DateTime? LastIntradayPollUtc { get; set; }
    public DateTime? LastSettleAttemptUtc { get; set; }
    public DateOnly? SettledThroughDate { get; set; }
    public DateOnly? LastRecheckDate { get; set; }
}

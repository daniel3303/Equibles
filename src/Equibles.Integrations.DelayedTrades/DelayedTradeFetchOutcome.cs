namespace Equibles.Integrations.DelayedTrades;

public enum DelayedTradeFetchOutcome
{
    Served = 0,

    // The venue answered that no file exists for the window (before the first print of a session, or a holiday).
    NoSession = 1,
}

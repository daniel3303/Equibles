namespace Equibles.Integrations.DelayedTrades;

// A venue publishes at most two post-trade files: the session in progress and the last settled one.
public enum DelayedTradeWindow
{
    CurrentSession = 0,
    PreviousSession = 1,
}

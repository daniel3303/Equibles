namespace Equibles.DelayedTrades.BusinessLogic.Bars;

public enum DelayedTradeBarOutcome
{
    Inserted,
    OverwroteYahoo,
    Rederived,
    Unchanged,
    SkippedBasis,
    SkippedInvalid,
    SkippedIdentity,
    Unsettled,
}

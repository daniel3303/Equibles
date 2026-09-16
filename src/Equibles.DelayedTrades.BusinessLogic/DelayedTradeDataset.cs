namespace Equibles.DelayedTrades.BusinessLogic;

public static class DelayedTradeDataset
{
    // The settled-session marker family; bump the suffix when the derivation rules change and every session must re-derive.
    public const string SettledBars = "delayed-trades-eod-v1";
}

namespace Equibles.DelayedTrades.BusinessLogic.Sessions;

public sealed class DelayedTradeSessionAggregation
{
    public List<DelayedTradeSessionBar> Bars { get; } = [];
    public int PrintCount { get; set; }
    public int LitPrintCount { get; set; }
    public int DarkPrintCount { get; set; }
    public int DuplicateCount { get; set; }
}

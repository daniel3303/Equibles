using Equibles.Integrations.DelayedTrades;

namespace Equibles.DelayedTrades.BusinessLogic.Sessions;

// One venue, one ISIN, one local session date; Open to Close come from price-forming prints only, volume from every counted print.
public sealed record DelayedTradeSessionBar(
    string Isin,
    string Venue,
    DateOnly SessionDate,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? Close,
    long Volume,
    long LitVolume,
    int PrintCount,
    int DarkPrintCount,
    DelayedTradePrint LastPriceForming
)
{
    public bool HasBar => Open != null && High != null && Low != null && Close != null;
}

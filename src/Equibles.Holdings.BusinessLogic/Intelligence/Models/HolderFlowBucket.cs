namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One bucket's money in and out of US equities over a quarter. Trades come only from filers
/// with a 13F the quarter before; the rest are counted but add no flow.
/// </summary>
public sealed record HolderFlowBucket
{
    public string Token { get; init; }
    public string Label { get; init; }
    public int Order { get; init; }
    public int Filers { get; init; }
    public int ComparedFilers { get; init; }
    public long EquityAum { get; init; }

    /// <summary>The compared filers' equity books the quarter before.</summary>
    public long PriorEquityAum { get; init; }

    public decimal BuyValue { get; init; }
    public decimal SellValue { get; init; }
    public int NetBuyers { get; init; }
    public int NetSellers { get; init; }
    public int UnpricedTrades { get; init; }

    /// <summary>
    /// Per sector, only when the quarter was read by sector: sectors by name, then
    /// exchange-traded products, then unclassified.
    /// </summary>
    public IReadOnlyList<HolderFlowSectorAmount> Sectors { get; init; } = [];

    public decimal NetValue => BuyValue - SellValue;

    /// <summary>Net flow over the compared filers' prior books; null without a prior book.</summary>
    public decimal? NetPercentOfPriorBook =>
        PriorEquityAum > 0 ? NetValue / PriorEquityAum * 100m : null;
}

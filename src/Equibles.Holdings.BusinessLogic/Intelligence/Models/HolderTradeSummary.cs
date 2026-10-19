namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// A filer's equity trading between two consecutive quarters. The trade fields are null when it
/// filed no 13F the quarter before, since nothing can be compared.
/// </summary>
public sealed record HolderTradeSummary
{
    public long EquityAum { get; init; }
    public long? PreviousEquityAum { get; init; }
    public decimal? BuyValue { get; init; }
    public decimal? SellValue { get; init; }

    /// <summary>The lesser of buys and sells over the average of both quarters' equity AUM.</summary>
    public decimal? Turnover { get; init; }

    public int? NewCount { get; init; }
    public int? ExitCount { get; init; }
    public int? IncreasedCount { get; init; }
    public int? ReducedCount { get; init; }

    /// <summary>Trades with no price on either side, left out of the buy and sell values.</summary>
    public int? UnpricedTradeCount { get; init; }

    /// <summary>Each priced trade behind the buy and sell values; empty without a comparison.</summary>
    public IReadOnlyList<HolderSecurityTrade> SecurityTrades { get; init; } = [];
}

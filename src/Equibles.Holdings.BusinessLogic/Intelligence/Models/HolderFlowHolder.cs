namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// One filer's measured quarter as flows read it: what buckets it, and its priced trades. The
/// trade fields are null when it filed no 13F the quarter before.
/// </summary>
public sealed record HolderFlowHolder
{
    public Guid InstitutionalHolderId { get; init; }
    public HolderMixHolder Mix { get; init; }
    public long EquityAum { get; init; }
    public long? PreviousEquityAum { get; init; }
    public decimal? BuyValue { get; init; }
    public decimal? SellValue { get; init; }
    public int? UnpricedTradeCount { get; init; }
}

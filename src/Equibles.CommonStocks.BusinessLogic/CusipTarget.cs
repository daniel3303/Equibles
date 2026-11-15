namespace Equibles.CommonStocks.BusinessLogic;

/// <summary>
/// The exact security a filed CUSIP resolves to: the filer's <see cref="CommonStockId"/>,
/// plus the listed ticker when the CUSIP identifies one of the filer's SECONDARY listings.
/// Null <see cref="ListedTicker"/> = the primary security (or a retired alias of it).
/// </summary>
public readonly record struct CusipTarget(Guid CommonStockId, string ListedTicker);

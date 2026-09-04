namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// Exact exchange listing identity. A CommonStock is the SEC filer, so one filer can own many
/// separately traded securities whose market datasets must never be combined.
/// </summary>
public readonly record struct ListedSecurityKey(Guid CommonStockId, string ListedTicker);

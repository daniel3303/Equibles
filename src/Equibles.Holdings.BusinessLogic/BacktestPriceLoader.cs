using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Loads adjusted-close price series and forward-fills them for the BusinessLogic backtests
/// (<see cref="FundScoringManager"/>, <see cref="SmartMoneyIndexManager"/>). Kept self-contained
/// in the BusinessLogic assembly so the scoring worker and the index have no dependency on the
/// web host.
/// </summary>
internal static class BacktestPriceLoader
{
    // Forward-fill needs a few trading days of pre-window history so day-zero resolves to the
    // last close even on a weekend or holiday.
    public const int PriceLookbackDays = 14;

    // OrderBy must precede the record projection — EF can't translate an OrderBy keyed off a
    // projected record's property because the constructor isn't translatable.
    public static Task<List<PriceRow>> LoadPrices(IQueryable<DailyStockPrice> query) =>
        query
            .OrderBy(p => p.Date)
            .Select(p => new PriceRow(p.CommonStockId, p.Date, p.AdjustedClose))
            .ToListAsync();

    public static decimal? ForwardFill(
        Dictionary<Guid, PriceRow[]> pricesByStock,
        Guid stockId,
        DateOnly date
    ) => pricesByStock.TryGetValue(stockId, out var series) ? ForwardFill(series, date) : null;

    // Largest close on or before `date` via binary search; null when the series starts later.
    public static decimal? ForwardFill(PriceRow[] series, DateOnly date)
    {
        if (series.Length == 0)
            return null;
        var lo = 0;
        var hi = series.Length - 1;
        var matchIdx = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (series[mid].Date <= date)
            {
                matchIdx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return matchIdx < 0 ? null : series[matchIdx].Price;
    }

    public readonly record struct PriceRow(Guid StockId, DateOnly Date, decimal Price);
}

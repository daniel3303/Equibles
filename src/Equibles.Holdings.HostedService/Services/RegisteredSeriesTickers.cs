using Equibles.CommonStocks.Data.Helpers;
using Equibles.CorporateActions.Data.Models;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Maps each issuer's listed tickers to the SEC fund series that owns them, from the exact class
/// tickers <see cref="FundSeries"/> keeps per series. Split restatement uses it to tell a trust's
/// separate funds apart from share classes of one security.
/// </summary>
internal static class RegisteredSeriesTickers
{
    /// <summary>
    /// Loads the map only for issuers holding a split attributed to an exact listing, the only
    /// kind the series rule can exempt.
    /// </summary>
    internal static async Task<Dictionary<Guid, IReadOnlyDictionary<string, string>>> Load(
        DbContext dbContext,
        IEnumerable<StockSplit> splits,
        CancellationToken cancellationToken
    )
    {
        var ids = splits
            .Where(split => split.EquityListingId != null)
            .Select(split => split.EquityIssuerId)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return [];

        var rows = await dbContext
            .Set<FundSeries>()
            .AsNoTracking()
            .Where(series =>
                series.EquityIssuerId != null
                && ids.Contains(series.EquityIssuerId.Value)
                && series.SeriesId != ""
            )
            .Select(series => new
            {
                series.EquityIssuerId,
                series.SeriesId,
                series.ClassTickers,
            })
            .ToListAsync(cancellationToken);

        var owned =
            new List<(Guid IssuerId, string SeriesId, IReadOnlyCollection<string> ClassTickers)>();
        foreach (var row in rows)
        {
            if (row.EquityIssuerId is { } issuerId)
                owned.Add((issuerId, row.SeriesId, row.ClassTickers));
        }
        return Build(owned);
    }

    /// <summary>
    /// A ticker claimed by two series identifies neither, so it is left out.
    /// </summary>
    internal static Dictionary<Guid, IReadOnlyDictionary<string, string>> Build(
        IEnumerable<(Guid IssuerId, string SeriesId, IReadOnlyCollection<string> ClassTickers)> rows
    )
    {
        return rows.Where(row => !string.IsNullOrWhiteSpace(row.SeriesId))
            .SelectMany(row =>
                (row.ClassTickers ?? [])
                    .Select(TickerNormalizer.NormalizeDashListed)
                    .Where(ticker => ticker != null)
                    .Select(ticker => (row.IssuerId, Ticker: ticker, SeriesId: row.SeriesId.Trim()))
            )
            .GroupBy(row => row.IssuerId)
            .ToDictionary(
                issuer => issuer.Key,
                issuer =>
                    (IReadOnlyDictionary<string, string>)
                        issuer
                            .GroupBy(row => row.Ticker, StringComparer.OrdinalIgnoreCase)
                            .Where(ticker =>
                                ticker
                                    .Select(row => row.SeriesId)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .Count() == 1
                            )
                            .ToDictionary(
                                ticker => ticker.Key,
                                ticker => ticker.First().SeriesId,
                                StringComparer.OrdinalIgnoreCase
                            )
            );
    }
}

using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Reads a stock's holder mix from its 13F rows: one stock-scoped read of the quarters, the same
/// like-for-like comparison the ownership history uses, and each holder's profile and measurements.
/// </summary>
[Service]
public class HolderMixReader
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;

    public HolderMixReader(
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository
    )
    {
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
    }

    /// <summary>
    /// The mix of every date but the oldest, which serves only as the first comparison. Share
    /// counts are restated to today's split basis per listing.
    /// </summary>
    /// <param name="listedTicker">An exact listing to read, or null for every class of the stock.</param>
    /// <param name="combinedReportDate">The open quarter shown as the combined view, if any.</param>
    public async Task<List<HolderMixQuarter>> Read(
        EquityIssuer stock,
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<StockSplit> splits,
        string listedTicker = null,
        DateOnly? combinedReportDate = null,
        CancellationToken cancellationToken = default
    )
    {
        var ordered = dates.Distinct().Order().ToArray();
        if (ordered.Length < 2)
            return [];
        var holdings =
            listedTicker == null
                ? _holdingRepository.Get13FHistoryByStock(stock)
                : _holdingRepository.Get13FHistoryByListing(stock, listedTicker);
        var rows = await holdings
            .Where(h => ordered.Contains(h.ReportDate))
            .GroupBy(h => new
            {
                h.ReportDate,
                h.InstitutionalHolderId,
                h.ListedTicker,
                h.ShareType,
                h.OptionType,
            })
            .Select(g => new
            {
                g.Key.ReportDate,
                g.Key.InstitutionalHolderId,
                g.Key.ListedTicker,
                g.Key.ShareType,
                g.Key.OptionType,
                Shares = g.Sum(h => h.Shares),
                Value = g.Sum(h => h.Value),
            })
            .ToListAsync(cancellationToken);

        // Every row proves its filer held something, as the ownership history counts presence.
        var holdersByDate = rows.GroupBy(row => row.ReportDate)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.InstitutionalHolderId).ToHashSet()
            );
        var comparisons = await HoldingsComparisonCoverage.CompareQuarters(
            _holdingRepository,
            ordered,
            holdersByDate,
            cancellationToken
        );

        var primaryTicker = stock.Presentation.Listing.Ticker;
        var scopes = rows.Select(row => row.ListedTicker ?? primaryTicker)
            .Distinct()
            .ToDictionary(
                series => series,
                series => ShareCountSplitScope.ForListedSeries(splits, primaryTicker, series)
            );
        // Principal amounts are not shares, so the mix leaves them out.
        var positions = rows.Where(row => row.ShareType == ShareType.Shares)
            .GroupBy(row => (row.ReportDate, row.InstitutionalHolderId))
            .Select(group =>
            {
                long Restated(OptionType? option) =>
                    group
                        .Where(row => row.OptionType == option)
                        .Sum(row =>
                            SplitAdjustment.AdjustShareCount(
                                row.Shares,
                                row.ReportDate,
                                scopes[row.ListedTicker ?? primaryTicker]
                            )
                        );
                long ValueOf(OptionType? option) =>
                    group.Where(row => row.OptionType == option).Sum(row => row.Value);
                return new HolderMixPosition
                {
                    ReportDate = group.Key.ReportDate,
                    InstitutionalHolderId = group.Key.InstitutionalHolderId,
                    Shares = Restated(null),
                    Value = ValueOf(null),
                    CallShares = Restated(OptionType.Call),
                    CallValue = ValueOf(OptionType.Call),
                    PutShares = Restated(OptionType.Put),
                    PutValue = ValueOf(OptionType.Put),
                };
            })
            .ToList();

        var measuredQuarters = new Dictionary<DateOnly, DateOnly?>();
        foreach (var date in ordered.Skip(1))
            measuredQuarters[date] = await _holderRepository.GetLatestMetricsReportDate(
                date,
                cancellationToken
            );
        var holderAt = await LoadHolders(
            rows.Select(row => row.InstitutionalHolderId).Distinct().ToList(),
            measuredQuarters.Values.OfType<DateOnly>().Distinct().ToList(),
            cancellationToken
        );

        return HolderMixCalculator.Build(
            ordered,
            positions,
            comparisons,
            measuredQuarters,
            holderAt,
            combinedReportDate
        );
    }

    private async Task<Func<Guid, DateOnly?, HolderMixHolder>> LoadHolders(
        List<Guid> holderIds,
        List<DateOnly> measured,
        CancellationToken cancellationToken
    )
    {
        var profiles = await _holderRepository
            .GetProfiles(holderIds)
            .AsNoTracking()
            .Select(profile => new
            {
                profile.InstitutionalHolderId,
                profile.EntityType,
                profile.ClientBase,
                profile.AdvisesPrivateFunds,
                profile.PrivateFundFocus,
            })
            .ToDictionaryAsync(profile => profile.InstitutionalHolderId, cancellationToken);
        var metrics =
            measured.Count == 0
                ? []
                : await _holderRepository
                    .GetQuarterlyMetrics(holderIds, measured)
                    .AsNoTracking()
                    .Select(row => new
                    {
                        row.InstitutionalHolderId,
                        row.ReportDate,
                        row.StyleBand,
                        row.TurnoverBand,
                        row.HorizonBand,
                        row.EquityAum,
                    })
                    .ToDictionaryAsync(
                        row => (row.InstitutionalHolderId, row.ReportDate),
                        cancellationToken
                    );
        var locations = await _holderRepository
            .GetAll()
            .AsNoTracking()
            .Where(holder => holderIds.Contains(holder.Id))
            .Select(holder => new { holder.Id, holder.StateOrCountry })
            .ToDictionaryAsync(
                holder => holder.Id,
                holder => holder.StateOrCountry,
                cancellationToken
            );

        return (holderId, quarter) =>
        {
            var profile = profiles.GetValueOrDefault(holderId);
            var measurement = quarter is { } date
                ? metrics.GetValueOrDefault((holderId, date))
                : null;
            return new HolderMixHolder
            {
                Profiled = profile != null,
                EntityType = profile?.EntityType ?? HolderEntityType.Unknown,
                ClientBase = profile?.ClientBase ?? HolderClientBase.Unknown,
                AdvisesPrivateFunds = profile?.AdvisesPrivateFunds,
                PrivateFundFocus = profile?.PrivateFundFocus ?? PrivateFundFocus.None,
                StyleBand = measurement?.StyleBand,
                TurnoverBand = measurement?.TurnoverBand,
                HorizonBand = measurement?.HorizonBand,
                EquityAum = measurement?.EquityAum,
                StateOrCountry = locations.GetValueOrDefault(holderId),
            };
        };
    }
}

using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
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
    public const int DefaultPeriods = 4;
    public const int MaxPeriods = 12;

    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly StockSplitRepository _stockSplitRepository;
    private readonly StockCombinedQuarterService _combinedQuarterService;
    private readonly HoldingsCorpusCoverage _corpusCoverage;

    public HolderMixReader(
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        StockSplitRepository stockSplitRepository,
        StockCombinedQuarterService combinedQuarterService,
        HoldingsCorpusCoverage corpusCoverage
    )
    {
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _stockSplitRepository = stockSplitRepository;
        _combinedQuarterService = combinedQuarterService;
        _corpusCoverage = corpusCoverage;
    }

    /// <summary>
    /// The mix of the listing's last <paramref name="periods"/> calendar quarters ending at its
    /// newest 13F report date, each compared with the quarter before. An ETF series or sibling
    /// class reads its exact listing; a primary stock's open quarter reads as the combined view.
    /// </summary>
    public async Task<HolderMixHistory> ReadRecent(
        EquityIssuer stock,
        string requestedTicker,
        int periods = DefaultPeriods,
        CancellationToken cancellationToken = default
    )
    {
        var listedTicker = SecondaryTickerPolicy.ResolveListedTicker(stock, requestedTicker);
        var exactListing = SecondaryTickerPolicy.RequiresExactListingScope(stock, listedTicker);
        var reportDates = exactListing
            ? await _holdingRepository.Get13FReportDatesByListingSnapshotBacked(
                stock,
                listedTicker,
                cancellationToken
            )
            : await _holdingRepository.Get13FReportDatesByStockSnapshotBacked(
                stock,
                cancellationToken
            );
        if (reportDates.Count == 0)
            return new HolderMixHistory
            {
                ListedTicker = listedTicker,
                ExactListing = exactListing,
                Unavailable = $"No institutional holdings data available for {requestedTicker}.",
            };

        var newest = reportDates.Max();
        var dates = new List<DateOnly> { newest };
        while (
            dates.Count <= Math.Clamp(periods, 1, MaxPeriods)
            && HoldingsCorpusCoverage.PreviousQuarterEnd(dates[^1])
                >= _corpusCoverage.CoverageStartDate
        )
            dates.Add(HoldingsCorpusCoverage.PreviousQuarterEnd(dates[^1]));
        if (dates.Count < 2)
            return new HolderMixHistory
            {
                ListedTicker = listedTicker,
                ExactListing = exactListing,
                Unavailable =
                    $"No quarter of {requestedTicker} before {newest:yyyy-MM-dd} is covered, so its holder mix has no comparison yet.",
            };

        // Carry-forward is filer-wide, so only a company's primary stock reads the combined view.
        var isPrimary = string.Equals(
            listedTicker,
            stock.Presentation.Listing.Ticker,
            StringComparison.OrdinalIgnoreCase
        );
        var anchor =
            isPrimary && !exactListing
                ? await _combinedQuarterService.Resolve(stock, cancellationToken)
                : null;
        var combinedDate =
            anchor is { IsCombined: true } && anchor.ReportDate == newest
                ? anchor.ReportDate
                : (DateOnly?)null;
        var splits = await _stockSplitRepository
            .GetEffectiveByStock(stock.Id, DateOnly.FromDateTime(DateTime.UtcNow))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var quarters = await Read(
            stock,
            dates,
            splits,
            exactListing ? listedTicker : null,
            combinedDate,
            cancellationToken
        );
        return new HolderMixHistory
        {
            ListedTicker = listedTicker,
            ExactListing = exactListing,
            Quarters = quarters,
            CarriedFrom = quarters.LastOrDefault() is { IsCombined: true } latest
                ? HoldingsCorpusCoverage.PreviousQuarterEnd(latest.ReportDate)
                : null,
        };
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

        // One MAX per column walks the ReportDate index backwards; a DISTINCT over every measured
        // quarter would read the whole index instead.
        var measuredQuarters = new Dictionary<DateOnly, DateOnly?>();
        foreach (var date in ordered.Skip(1))
            measuredQuarters[date] = await _holderRepository.GetLatestMetricsReportDate(
                date,
                cancellationToken
            );
        // A successor across a CIK change can hold none of the stock and still take its
        // predecessor's position, so every succession member needs its profile too.
        var holderIds = rows.Select(row => row.InstitutionalHolderId)
            .Concat(
                comparisons.Values.SelectMany(comparison =>
                    comparison.Successions.SelectMany(group =>
                        group.Predecessors.Keys.Concat(group.Successors.Keys)
                    )
                )
            )
            .Distinct()
            .ToList();
        var holderAt = await LoadHolders(
            holderIds,
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
                Profile = new HolderMixProfile(
                    profile.EntityType,
                    profile.ClientBase,
                    profile.AdvisesPrivateFunds,
                    profile.PrivateFundFocus
                ),
            })
            .ToDictionaryAsync(
                profile => profile.InstitutionalHolderId,
                profile => profile.Profile,
                cancellationToken
            );
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
            return HolderMixHolder.Of(
                profile,
                measurement?.StyleBand,
                measurement?.TurnoverBand,
                measurement?.HorizonBand,
                measurement?.EquityAum,
                locations.GetValueOrDefault(holderId)
            );
        };
    }
}

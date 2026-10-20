using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// The 13F lane: each filer's common shares in the listing for the presented quarter (the open
/// window's combined view on a primary stock, as filed otherwise), compared with the filer's own
/// 13F for the quarter before the row's report date. Principal amounts and option legs are left out.
/// </summary>
public class Form13FContributor : IHolderSourceContributor
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly HoldingsCorpusCoverage _corpusCoverage;

    public Form13FContributor(
        InstitutionalHoldingRepository holdingRepository,
        InstitutionalHolderRepository holderRepository,
        HoldingsCorpusCoverage corpusCoverage = null
    )
    {
        _holdingRepository = holdingRepository;
        _holderRepository = holderRepository;
        _corpusCoverage = corpusCoverage ?? HoldingsCorpusCoverage.Default;
    }

    public async Task<IReadOnlyList<StockHolderRow>> Read(
        StockOwnershipScope scope,
        CancellationToken cancellationToken = default
    )
    {
        if (scope.Anchor is not { } anchor)
            return [];
        var stock = scope.Stock;
        var presented =
            anchor.IsCombined
                ? _holdingRepository.GetCombinedQuarterByStock(
                    stock,
                    anchor.ReportDate,
                    anchor.PreviousReportDate!.Value
                )
            : scope.ExactListing
                ? _holdingRepository.Get13FByListing(stock, scope.ListedTicker, anchor.ReportDate)
            : _holdingRepository.Get13FByStock(stock, anchor.ReportDate);
        var positions = await presented
            .Where(h => h.ShareType == ShareType.Shares && h.OptionType == null)
            .AsNoTracking()
            .Select(h => new
            {
                h.InstitutionalHolderId,
                h.ReportDate,
                h.ListedTicker,
                h.Shares,
                h.Value,
                Unvalued = h.ValuePending || h.ValueUnavailable,
                h.FilingDate,
                h.AccessionNumber,
                h.InstitutionalHolder.Cik,
                h.InstitutionalHolder.Name,
                h.InstitutionalHolder.StateOrCountry,
            })
            .ToListAsync(cancellationToken);
        if (positions.Count == 0)
            return [];

        // The quarter before each presented report date, for the filer's own previous 13F.
        var dates = positions
            .Select(p => p.ReportDate)
            .Distinct()
            .SelectMany(date => new[] { date, HoldingsCorpusCoverage.PreviousQuarterEnd(date) })
            .Where(date => date >= _corpusCoverage.CoverageStartDate)
            .Distinct()
            .Order()
            .ToArray();
        var history = scope.ExactListing
            ? _holdingRepository.Get13FHistoryByListing(stock, scope.ListedTicker)
            : _holdingRepository.Get13FHistoryByStock(stock);
        var compared = await history
            .Where(h =>
                dates.Contains(h.ReportDate)
                && h.ShareType == ShareType.Shares
                && h.OptionType == null
            )
            .GroupBy(h => new
            {
                h.ReportDate,
                h.InstitutionalHolderId,
                h.ListedTicker,
            })
            .Select(g => new
            {
                g.Key.ReportDate,
                g.Key.InstitutionalHolderId,
                g.Key.ListedTicker,
                Shares = g.Sum(h => h.Shares),
            })
            .ToListAsync(cancellationToken);
        var restatedAt = compared
            .GroupBy(row => (row.ReportDate, row.InstitutionalHolderId))
            .ToDictionary(
                group => group.Key,
                group =>
                    group.Sum(row => scope.Restate(row.Shares, row.ReportDate, row.ListedTicker))
            );
        var comparisons = await HoldingsComparisonCoverage.CompareQuarters(
            _holdingRepository,
            dates,
            compared
                .GroupBy(row => row.ReportDate)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.InstitutionalHolderId).ToHashSet()
                ),
            cancellationToken
        );

        var priorShares = PriorShares(comparisons, restatedAt);

        var holderIds = positions.Select(p => p.InstitutionalHolderId).Distinct().ToList();
        var reportDates = positions.Select(p => p.ReportDate).Distinct().ToList();
        var books = await _holderRepository
            .GetQuarterlyMetrics(holderIds, reportDates)
            .AsNoTracking()
            .Select(metrics => new
            {
                metrics.InstitutionalHolderId,
                metrics.ReportDate,
                metrics.EquityAum,
            })
            .ToDictionaryAsync(
                metrics => (metrics.InstitutionalHolderId, metrics.ReportDate),
                metrics => metrics.EquityAum,
                cancellationToken
            );

        var rows = new List<StockHolderRow>();
        foreach (var holder in positions.GroupBy(p => p.InstitutionalHolderId))
        {
            var first = holder.First();
            var date = first.ReportDate;
            var shares = holder.Sum(p => scope.Restate(p.Shares, p.ReportDate, p.ListedTicker));
            var value = holder.All(p => p.Unvalued) ? (long?)null : holder.Sum(p => p.Value);
            // Only a filer with an observed 13F in both quarters is compared, so a missing
            // prior position is a new one and a filer that did not file reads no change.
            var status = comparisons.GetValueOrDefault(date);
            var comparable = status != null && status.CanCompareHolder(holder.Key);
            long? change = comparable
                ? shares - priorShares[date].GetValueOrDefault(holder.Key)
                : null;
            DateOnly? priorAsOf = comparable ? status.PreviousReportDate : null;
            var book = books.GetValueOrDefault((holder.Key, date));
            var cik = CikNormalizer.Canonicalize(first.Cik);
            rows.Add(
                new StockHolderRow
                {
                    HolderKey = new StockHolderKey(
                        cik == null ? StockHolderKeyKind.Opaque : StockHolderKeyKind.Cik,
                        cik ?? holder.Key.ToString()
                    ),
                    Source = StockHolderSource.Form13F,
                    Name = first.Name,
                    Cik = cik,
                    InstitutionalHolderId = holder.Key,
                    Detail = ClassesHeld(holder.Select(p => p.ListedTicker), scope),
                    Shares = shares,
                    FiledShares = holder.Sum(p => p.Shares),
                    Value = value,
                    ChangeShares = change,
                    PriorAsOf = priorAsOf,
                    PortfolioWeight =
                        value is { } v && book > 0 ? Math.Round(100m * v / book, 4) : null,
                    AsOf = date,
                    FilingDate = holder.Max(p => p.FilingDate),
                    Accession = holder
                        .OrderByDescending(p => p.FilingDate)
                        .Select(p => p.AccessionNumber)
                        .FirstOrDefault(),
                    Carried = anchor.IsCombined && date != anchor.ReportDate,
                    Country = first.StateOrCountry,
                    Flags = holder.Any(p => scope.SplitUnresolved(p.ReportDate, p.ListedTicker))
                        ? StockHolderRowFlags.SplitUnresolved
                        : StockHolderRowFlags.None,
                }
            );
        }
        return rows;
    }

    // A filer holding more than one class of the issuer has its classes summed as filed, the way
    // the entity-wide shares outstanding sums them; the row names them.
    private static string ClassesHeld(IEnumerable<string> listedTickers, StockOwnershipScope scope)
    {
        var classes = listedTickers
            .Select(ticker => ticker ?? scope.PrimaryTicker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        return classes.Count > 1 ? $"Classes {string.Join(", ", classes)}" : null;
    }

    // Each filer's restated shares the quarter before a compared quarter, keyed by the holder it is
    // compared under: the successor that alone continues a predecessor, or the filer itself.
    private static Dictionary<DateOnly, Dictionary<Guid, long>> PriorShares(
        IReadOnlyDictionary<DateOnly, HoldingsComparisonStatus> comparisons,
        IReadOnlyDictionary<(DateOnly, Guid), long> restatedAt
    )
    {
        var byDate = new Dictionary<DateOnly, Dictionary<Guid, long>>();
        foreach (var (date, status) in comparisons)
        {
            var prior = new Dictionary<Guid, long>();
            foreach (var ((reportDate, holderId), shares) in restatedAt)
            {
                if (reportDate != status.PreviousReportDate)
                    continue;
                var key = status.ComparisonKey(holderId);
                prior[key] = prior.GetValueOrDefault(key) + shares;
            }
            byDate[date] = prior;
        }
        return byDate;
    }
}

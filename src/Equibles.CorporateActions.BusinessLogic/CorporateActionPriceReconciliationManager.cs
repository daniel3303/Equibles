using System.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CorporateActions.BusinessLogic;

/// <summary>
/// Coordinates full price-history reconciliation after captured splits or cash dividends change.
/// </summary>
[Service]
public class CorporateActionPriceReconciliationManager
{
    private readonly StockSplitRepository _splitRepository;
    private readonly CashDividendRepository _dividendRepository;
    private readonly CommonStockRepository _stockRepository;
    private readonly CorporateActionPriceReconciliationCursorRepository _cursorRepository;

    public CorporateActionPriceReconciliationManager(
        StockSplitRepository splitRepository,
        CashDividendRepository dividendRepository,
        CommonStockRepository stockRepository,
        CorporateActionPriceReconciliationCursorRepository cursorRepository
    )
    {
        _splitRepository = splitRepository;
        _dividendRepository = dividendRepository;
        _stockRepository = stockRepository;
        _cursorRepository = cursorRepository;
    }

    /// <summary>
    /// Returns distinct exact listed series with at least one unreconciled corporate action.
    /// Splits retain their captured series; stock-level dividends target the current primary.
    /// Actions stay pending through their effective date so the provider history is not stamped
    /// before that session has settled and incorporated the adjustment.
    /// </summary>
    public async Task<PendingPriceReconciliationSelection> SelectPendingSeries(
        int maxPerCycle,
        DateOnly settledBefore,
        CancellationToken cancellationToken = default
    )
    {
        await using var transaction = await _stockRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var cursor = await GetCursorForUpdate(cancellationToken);
        var splits = await LoadPendingSplits(settledBefore, cancellationToken);
        var dividends = await LoadPendingDividends(settledBefore, cancellationToken);
        var pendingSeries = splits
            .Keys.Concat(dividends.Keys)
            .Distinct()
            .OrderBy(key => key.CommonStockId)
            .ThenBy(key => key.ListedTicker, StringComparer.Ordinal)
            .Select(key => new PendingPriceReconciliationSeries(
                key.CommonStockId,
                key.ListedTicker,
                splits.TryGetValue(key, out var pendingSplits) ? pendingSplits : [],
                dividends.TryGetValue(key, out var pendingDividends) ? pendingDividends : []
            ))
            .ToList();
        var fairOrder = RotateAfterCursor(pendingSeries, cursor);
        var selected = maxPerCycle > 0 ? fairOrder.Take(maxPerCycle).ToList() : fairOrder;

        if (selected.Count > 0)
        {
            var last = selected[^1];
            cursor.LastCommonStockId = last.CommonStockId;
            cursor.LastListedTicker = last.ListedTicker;
            cursor.UpdatedAt = DateTime.UtcNow;
            await _cursorRepository.SaveChanges();
        }

        await transaction.CommitAsync(cancellationToken);

        return new PendingPriceReconciliationSelection(
            selected,
            pendingSeries.Count,
            pendingSeries.Count - selected.Count
        );
    }

    private async Task<CorporateActionPriceReconciliationCursor> GetCursorForUpdate(
        CancellationToken cancellationToken
    )
    {
        var cursor = await _cursorRepository.GetForUpdate(
            CorporateActionPriceReconciliationCursor.DefaultName,
            cancellationToken
        );
        if (cursor != null)
            return cursor;

        cursor = new CorporateActionPriceReconciliationCursor
        {
            Name = CorporateActionPriceReconciliationCursor.DefaultName,
        };
        _cursorRepository.Add(cursor);
        await _cursorRepository.SaveChanges();
        return cursor;
    }

    private static List<PendingPriceReconciliationSeries> RotateAfterCursor(
        IReadOnlyList<PendingPriceReconciliationSeries> pendingSeries,
        CorporateActionPriceReconciliationCursor cursor
    )
    {
        if (
            pendingSeries.Count == 0
            || cursor.LastCommonStockId == null
            || cursor.LastListedTicker == null
        )
            return pendingSeries.ToList();

        var cursorKey = new PriceReconciliationKey(
            cursor.LastCommonStockId.Value,
            cursor.LastListedTicker
        );
        var start =
            pendingSeries
                .Select(
                    (series, index) =>
                        new
                        {
                            Key = new PriceReconciliationKey(
                                series.CommonStockId,
                                series.ListedTicker
                            ),
                            Index = index,
                        }
                )
                .FirstOrDefault(item => Compare(item.Key, cursorKey) > 0)
                ?.Index
            ?? 0;

        return pendingSeries.Skip(start).Concat(pendingSeries.Take(start)).ToList();
    }

    private static int Compare(PriceReconciliationKey left, PriceReconciliationKey right)
    {
        var stockComparison = left.CommonStockId.CompareTo(right.CommonStockId);
        return stockComparison != 0
            ? stockComparison
            : string.Compare(left.ListedTicker, right.ListedTicker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stamps only selected actions whose source state is unchanged after the provider fetch.
    /// </summary>
    public Task<int> StampApplied(
        PendingPriceReconciliationSeries selectedSeries,
        DateTime appliedTime,
        CancellationToken cancellationToken = default
    ) =>
        StampAppliedCore(
            selectedSeries,
            [],
            false,
            DateOnly.MinValue,
            appliedTime,
            cancellationToken
        );

    /// <summary>
    /// Stamps unchanged selected splits plus dividends whose current state exactly matches the
    /// same provider response that supplied the replacement adjusted-price series.
    /// </summary>
    public Task<int> StampApplied(
        PendingPriceReconciliationSeries selectedSeries,
        IReadOnlyCollection<CapturedDividend> priceSeriesDividends,
        DateOnly settledBefore,
        DateTime appliedTime,
        CancellationToken cancellationToken = default
    )
    {
        return StampAppliedCore(
            selectedSeries,
            priceSeriesDividends,
            true,
            settledBefore,
            appliedTime,
            cancellationToken
        );
    }

    public Task<int> StampAppliedHistorical(
        PendingPriceReconciliationSeries selectedSeries,
        IReadOnlyCollection<CapturedDividend> priceSeriesDividends,
        DateOnly settledBefore,
        DateTime appliedTime,
        Guid historicalListingId,
        DateOnly expectedDelistedOn,
        CancellationToken cancellationToken = default
    )
    {
        return StampAppliedCore(
            selectedSeries,
            priceSeriesDividends,
            true,
            settledBefore,
            appliedTime,
            cancellationToken,
            expectedDelistedOn: expectedDelistedOn,
            expectedHistoricalListingId: historicalListingId
        );
    }

    /// <summary>
    /// Stamps actions only when the locked issuer still has the listing state that bounded the
    /// provider response. This prevents a delisting or reactivation race from certifying stale
    /// price history.
    /// </summary>
    public Task<int> StampApplied(
        PendingPriceReconciliationSeries selectedSeries,
        IReadOnlyCollection<CapturedDividend> priceSeriesDividends,
        DateOnly settledBefore,
        DateTime appliedTime,
        bool expectedActive,
        DateOnly? expectedDelistedOn,
        CancellationToken cancellationToken = default
    )
    {
        return StampAppliedCore(
            selectedSeries,
            priceSeriesDividends,
            true,
            settledBefore,
            appliedTime,
            cancellationToken,
            expectedActive,
            expectedDelistedOn
        );
    }

    /// <summary>
    /// Clears reconciliation markers whose stored price boundary proves that the replacement did
    /// not put the series on one split basis. The next selection pass retries those exact series.
    /// </summary>
    public async Task<int> RequeueAppliedSplits(
        IReadOnlyCollection<AppliedSplitMarkerSnapshot> auditedMarkers,
        CancellationToken cancellationToken = default
    )
    {
        if (auditedMarkers.Count == 0)
            return 0;

        var appliedTimesById = auditedMarkers
            .GroupBy(marker => marker.SplitId)
            .ToDictionary(group => group.Key, group => group.Last().AppliedTime);

        await using var transaction = await _stockRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var splits = await _splitRepository.GetForUpdate(appliedTimesById.Keys, cancellationToken);
        var applied = splits
            .Where(split =>
                split.PriceAdjustmentAppliedTime == appliedTimesById.GetValueOrDefault(split.Id)
            )
            .ToList();

        foreach (var split in applied)
            split.PriceAdjustmentAppliedTime = null;

        if (applied.Count > 0)
            await _splitRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return applied.Count;
    }

    private async Task<int> StampAppliedCore(
        PendingPriceReconciliationSeries selectedSeries,
        IReadOnlyCollection<CapturedDividend> priceSeriesDividends,
        bool requirePriceSeriesDividendMatch,
        DateOnly settledBefore,
        DateTime appliedTime,
        CancellationToken cancellationToken,
        bool? expectedActive = null,
        DateOnly? expectedDelistedOn = null,
        Guid? expectedHistoricalListingId = null
    )
    {
        await using var transaction = await _stockRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var stock = await _stockRepository.GetForUpdate(
            selectedSeries.CommonStockId,
            cancellationToken
        );
        if (stock == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }
        if (expectedHistoricalListingId != null)
        {
            var listing = await _stockRepository.GetDelistedListingForUpdate(
                expectedHistoricalListingId.Value,
                cancellationToken
            );
            if (
                listing == null
                || listing.CommonStockId != stock.Id
                || listing.ListedTicker != selectedSeries.ListedTicker
                || listing.DelistedOn != expectedDelistedOn
            )
            {
                await transaction.RollbackAsync(cancellationToken);
                return 0;
            }
        }
        else if (
            expectedActive != null
            && (stock.Active != expectedActive.Value || stock.DelistedOn != expectedDelistedOn)
        )
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        var unchangedSplits = await LoadUnchangedSplits(selectedSeries, cancellationToken);
        var isStillPrimary = string.Equals(
            stock.Ticker,
            selectedSeries.ListedTicker,
            StringComparison.OrdinalIgnoreCase
        );
        var dividendsToStamp = requirePriceSeriesDividendMatch
            ? await LoadResponseMatchedDividends(
                selectedSeries,
                priceSeriesDividends,
                settledBefore,
                isStillPrimary,
                cancellationToken
            )
            : await LoadUnchangedDividends(selectedSeries, isStillPrimary, cancellationToken);
        ApplyMarkers(unchangedSplits, dividendsToStamp, appliedTime);

        var stamped = unchangedSplits.Count + dividendsToStamp.Count;
        if (stamped > 0)
            await _stockRepository.SaveChanges();

        await transaction.CommitAsync(cancellationToken);
        return stamped;
    }

    private async Task<
        Dictionary<PriceReconciliationKey, IReadOnlyList<PendingSplitSnapshot>>
    > LoadPendingSplits(DateOnly settledBefore, CancellationToken cancellationToken)
    {
        var rows = await _splitRepository
            .GetPendingPriceAdjustment()
            .Where(split => split.PriceSeriesTicker != null && split.EffectiveDate < settledBefore)
            .Select(split => new
            {
                split.Id,
                split.CommonStockId,
                split.PriceSeriesTicker,
                split.EffectiveDate,
                split.Numerator,
                split.Denominator,
                split.Source,
            })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(row => new PriceReconciliationKey(
                row.CommonStockId,
                row.PriceSeriesTicker
            ))
            .ToDictionary(
                group => group.Key,
                group =>
                    (IReadOnlyList<PendingSplitSnapshot>)
                        group
                            .OrderBy(row => row.EffectiveDate)
                            .ThenBy(row => row.Id)
                            .Select(row => new PendingSplitSnapshot(
                                row.Id,
                                row.EffectiveDate,
                                row.Numerator,
                                row.Denominator,
                                row.Source
                            ))
                            .ToList()
            );
    }

    private async Task<
        Dictionary<PriceReconciliationKey, IReadOnlyList<PendingDividendSnapshot>>
    > LoadPendingDividends(DateOnly settledBefore, CancellationToken cancellationToken)
    {
        var rows = await _dividendRepository
            .GetPendingPriceAdjustment()
            .Where(dividend => dividend.ExDate < settledBefore)
            .Select(dividend => new
            {
                dividend.Id,
                dividend.CommonStockId,
                dividend.ExDate,
                dividend.AmountPerShare,
                dividend.Source,
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return [];

        var stockIds = rows.Select(row => row.CommonStockId).Distinct().ToList();
        var primaryTickers = await _stockRepository
            .GetByIds(stockIds)
            .Select(stock => new { stock.Id, stock.Ticker })
            .ToDictionaryAsync(stock => stock.Id, stock => stock.Ticker, cancellationToken);

        return rows.Where(row => primaryTickers.ContainsKey(row.CommonStockId))
            .GroupBy(row => new PriceReconciliationKey(
                row.CommonStockId,
                primaryTickers[row.CommonStockId]
            ))
            .ToDictionary(
                group => group.Key,
                group =>
                    (IReadOnlyList<PendingDividendSnapshot>)
                        group
                            .OrderBy(row => row.ExDate)
                            .ThenBy(row => row.Id)
                            .Select(row => new PendingDividendSnapshot(
                                row.Id,
                                row.ExDate,
                                row.AmountPerShare,
                                row.Source
                            ))
                            .ToList()
            );
    }

    private async Task<List<StockSplit>> LoadUnchangedSplits(
        PendingPriceReconciliationSeries selectedSeries,
        CancellationToken cancellationToken
    )
    {
        var selectedById = selectedSeries.Splits.ToDictionary(split => split.Id);
        var locked = await _splitRepository.GetForUpdate(selectedById.Keys, cancellationToken);

        return locked
            .Where(split =>
                split.CommonStockId == selectedSeries.CommonStockId
                && split.PriceSeriesTicker == selectedSeries.ListedTicker
                // Keep the selection and stamping predicates identical so a legacy premature
                // marker can be replaced with a post-effective marker after the provider fetch.
                && !split.IsPriceAdjustmentApplied()
            )
            .Where(split =>
            {
                if (!selectedById.TryGetValue(split.Id, out var selected))
                    return false;

                return split.EffectiveDate == selected.EffectiveDate
                    && split.Numerator == selected.Numerator
                    && split.Denominator == selected.Denominator
                    && split.Source == selected.Source;
            })
            .ToList();
    }

    private async Task<List<CashDividend>> LoadUnchangedDividends(
        PendingPriceReconciliationSeries selectedSeries,
        bool isStillPrimary,
        CancellationToken cancellationToken
    )
    {
        if (!isStillPrimary)
            return [];

        var selectedById = selectedSeries.Dividends.ToDictionary(dividend => dividend.Id);
        var locked = await _dividendRepository.GetForUpdate(selectedById.Keys, cancellationToken);

        return locked
            .Where(dividend =>
                dividend.CommonStockId == selectedSeries.CommonStockId
                && (
                    dividend.PriceAdjustmentAppliedTime == null
                    || dividend.PriceAdjustmentAppliedAmountPerShare != dividend.AmountPerShare
                )
            )
            .Where(dividend =>
            {
                if (!selectedById.TryGetValue(dividend.Id, out var selected))
                    return false;

                return dividend.ExDate == selected.ExDate
                    && dividend.AmountPerShare == selected.AmountPerShare
                    && dividend.Source == selected.Source;
            })
            .ToList();
    }

    private async Task<List<CashDividend>> LoadResponseMatchedDividends(
        PendingPriceReconciliationSeries selectedSeries,
        IReadOnlyCollection<CapturedDividend> priceSeriesDividends,
        DateOnly settledBefore,
        bool isStillPrimary,
        CancellationToken cancellationToken
    )
    {
        if (!isStillPrimary || priceSeriesDividends.Count == 0)
            return [];

        var expectedByDate = CashDividendCaptureManager
            .CombineSameDateDividends(priceSeriesDividends)
            .Where(dividend => dividend.ExDate < settledBefore)
            .ToDictionary(dividend => dividend.ExDate, dividend => dividend.AmountPerShare);
        if (expectedByDate.Count == 0)
            return [];

        var expectedDates = expectedByDate.Keys.ToList();
        var candidateIds = await _dividendRepository
            .GetByStock(selectedSeries.CommonStockId)
            .Where(dividend => expectedDates.Contains(dividend.ExDate))
            .Select(dividend => dividend.Id)
            .ToListAsync(cancellationToken);
        var locked = await _dividendRepository.GetForUpdate(candidateIds, cancellationToken);

        return locked
            .Where(dividend =>
                dividend.PriceAdjustmentAppliedTime == null
                || dividend.PriceAdjustmentAppliedAmountPerShare != dividend.AmountPerShare
            )
            .Where(dividend =>
                expectedByDate.TryGetValue(dividend.ExDate, out var expectedAmount)
                && dividend.AmountPerShare == expectedAmount
            )
            .ToList();
    }

    private static void ApplyMarkers(
        IReadOnlyCollection<StockSplit> splits,
        IReadOnlyCollection<CashDividend> dividends,
        DateTime appliedTime
    )
    {
        foreach (var split in splits)
            split.PriceAdjustmentAppliedTime = appliedTime;

        foreach (var dividend in dividends)
        {
            dividend.PriceAdjustmentAppliedAmountPerShare = dividend.AmountPerShare;
            dividend.PriceAdjustmentAppliedTime = appliedTime;
        }
    }
}

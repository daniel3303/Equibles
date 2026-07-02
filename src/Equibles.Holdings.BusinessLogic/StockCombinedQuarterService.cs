using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// The one place that decides how a stock's newest 13F quarter is presented. While the 45-day
/// filing window is open the quarter only holds the funds that filed early, so positions are
/// served as the COMBINED view (new-quarter filings + prior-quarter carry-forward for funds yet
/// to file; a fund that filed without the stock is a proven exit) and quarter-over-quarter
/// figures are computed over reported filings only. After the window closes the quarter is
/// served as filed. Web pages, MCP tools and agent tools all resolve through this service so
/// "the current quarter" means the same thing everywhere.
/// </summary>
[Service]
public class StockCombinedQuarterService
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly StockSplitRepository _stockSplitRepository;

    public StockCombinedQuarterService(
        InstitutionalHoldingRepository holdingRepository,
        StockSplitRepository stockSplitRepository
    )
    {
        _holdingRepository = holdingRepository;
        _stockSplitRepository = stockSplitRepository;
    }

    /// <summary>Resolves the stock's newest 13F quarter and how it must be presented.</summary>
    public Task<StockQuarterAnchor> Resolve(
        CommonStock stock,
        CancellationToken cancellationToken = default
    )
    {
        return Resolve(stock, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    }

    // Explicit-today overload so callers and tests can pin the clock.
    public async Task<StockQuarterAnchor> Resolve(
        CommonStock stock,
        DateOnly today,
        CancellationToken cancellationToken = default
    )
    {
        var dates = await _holdingRepository
            .Get13FReportDatesByStock(stock)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (dates.Count == 0)
            return null;

        return new StockQuarterAnchor
        {
            ReportDate = dates[0],
            PreviousReportDate = dates.Count > 1 ? dates[1] : null,
            FilingWindowOpen = CombinedQuarterHelper.IsFilingWindowOpen(dates[0], today),
        };
    }

    /// <summary>
    /// The positions to present for the anchored quarter: the combined view while the filing
    /// window is open, the as-filed 13F rows afterwards.
    /// </summary>
    public IQueryable<InstitutionalHolding> GetPresentedPositions(
        CommonStock stock,
        StockQuarterAnchor anchor
    )
    {
        return anchor.IsCombined
            ? _holdingRepository.GetCombinedQuarterByStock(
                stock,
                anchor.ReportDate,
                anchor.PreviousReportDate.Value
            )
            : _holdingRepository.Get13FByStock(stock, anchor.ReportDate);
    }

    /// <summary>Same positions with the holder navigation eagerly loaded for rendering.</summary>
    public IQueryable<InstitutionalHolding> GetPresentedPositionsWithHolder(
        CommonStock stock,
        StockQuarterAnchor anchor
    )
    {
        return anchor.IsCombined
            ? _holdingRepository.GetCombinedQuarterByStockWithHolder(
                stock,
                anchor.ReportDate,
                anchor.PreviousReportDate.Value
            )
            : _holdingRepository.Get13FByStockWithHolder(stock, anchor.ReportDate);
    }

    /// <summary>
    /// Reported-so-far activity for a combined anchor: what the funds that already filed the
    /// new quarter did in this stock, plus the combined-view totals. Only meaningful while
    /// <see cref="StockQuarterAnchor.IsCombined"/>; three small per-stock reads, aggregated in
    /// memory.
    /// </summary>
    public async Task<StockReportedActivity> LoadReportedActivity(
        CommonStock stock,
        StockQuarterAnchor anchor,
        CancellationToken cancellationToken = default
    )
    {
        if (!anchor.IsCombined)
            throw new InvalidOperationException(
                "Reported activity is only defined for a combined anchor (open filing window "
                    + "with a previous quarter to compare against)."
            );

        var current = await _holdingRepository
            .Get13FByStock(stock, anchor.ReportDate)
            .Select(h => new
            {
                h.InstitutionalHolderId,
                h.Shares,
                h.Value,
            })
            .ToListAsync(cancellationToken);
        var previous = await _holdingRepository
            .Get13FByStock(stock, anchor.PreviousReportDate.Value)
            .Select(h => new
            {
                h.InstitutionalHolderId,
                h.Shares,
                h.Value,
            })
            .ToListAsync(cancellationToken);

        var currentHolders = current.Select(h => h.InstitutionalHolderId).ToHashSet();
        var previousHolders = previous.Select(h => h.InstitutionalHolderId).ToHashSet();

        // Previous holders who filed the new quarter ANYWHERE — present in it or proven exits.
        var filedPreviousHolders = (
            await _holdingRepository
                .GetFiledHolderIdsAmong(anchor.ReportDate, previousHolders.ToList())
                .ToListAsync(cancellationToken)
        ).ToHashSet();

        var carried = previous
            .Where(h => !filedPreviousHolders.Contains(h.InstitutionalHolderId))
            .ToList();

        // Restate both quarters' share counts onto today's post-split basis before any sum —
        // a split falling between the two quarters while the window is open would otherwise
        // read as every continuing filer doubling/halving its position (the same restatement
        // every other 13F comparison surface applies). Dollar values are split-invariant.
        var splits = await _stockSplitRepository
            .GetByStock(stock.Id)
            .ToListAsync(cancellationToken);
        var currentFactor = SplitAdjustment.ShareCountFactor(anchor.ReportDate, splits);
        var previousFactor = SplitAdjustment.ShareCountFactor(
            anchor.PreviousReportDate.Value,
            splits
        );
        var currentShares = SplitAdjustment.AdjustShareCount(
            current.Sum(h => h.Shares),
            currentFactor
        );
        var reportedPreviousShares = SplitAdjustment.AdjustShareCount(
            previous
                .Where(h => filedPreviousHolders.Contains(h.InstitutionalHolderId))
                .Sum(h => h.Shares),
            previousFactor
        );
        var carriedShares = SplitAdjustment.AdjustShareCount(
            carried.Sum(h => h.Shares),
            previousFactor
        );

        return new StockReportedActivity
        {
            PreviousHolderCount = previousHolders.Count,
            ReportedFilerCount = currentHolders.Union(filedPreviousHolders).Count(),
            NewFilerCount = currentHolders.Count(id => !previousHolders.Contains(id)),
            SoldOutFilerCount = filedPreviousHolders.Count(id => !currentHolders.Contains(id)),
            // Net over reporters only: their new shares (zero for exits) minus their previous
            // shares (zero for new positions). Carried positions contribute nothing.
            NetReportedShareDelta = currentShares - reportedPreviousShares,
            CombinedHolderCount = currentHolders
                .Union(carried.Select(h => h.InstitutionalHolderId))
                .Count(),
            CombinedShares = currentShares + carriedShares,
            CombinedValue = current.Sum(h => h.Value) + carried.Sum(h => h.Value),
        };
    }
}

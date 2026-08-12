using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
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
        var dates = (
            await _holdingRepository.Get13FReportDatesByStockSnapshotBacked(
                stock,
                cancellationToken
            )
        )
            .Take(2)
            .ToList();
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
    /// <see cref="StockQuarterAnchor.IsCombined"/>. Both quarter modes come from the materialized
    /// stock/listing snapshots; only a missing generation uses the repository's bounded two-quarter
    /// fallback.
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

        var asFiled = (
            await _holdingRepository.GetStockActivitySnapshotsByStockSnapshotBacked(
                stock,
                cancellationToken
            )
        ).SingleOrDefault(row => row.ReportDate == anchor.ReportDate);
        var combined = await _holdingRepository.GetCombinedStockActivitySnapshotBacked(
            stock,
            anchor.ReportDate,
            anchor.PreviousReportDate.Value,
            cancellationToken
        );
        if (asFiled == null || combined == null)
        {
            throw new InvalidOperationException(
                $"Combined holdings activity is unavailable for {stock.Ticker} on {anchor.ReportDate:yyyy-MM-dd}."
            );
        }

        var splits = await _stockSplitRepository
            .GetByStock(stock.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var combinedShares = MarketActivityShareRestater.RestateListingTotal(
            combined.ListingShares,
            listing => listing.CurrentShares,
            anchor.ReportDate,
            stock.Ticker,
            splits
        );
        var previousShares = MarketActivityShareRestater.RestateListingTotal(
            combined.ListingShares,
            listing => listing.PreviousShares,
            anchor.PreviousReportDate.Value,
            stock.Ticker,
            splits
        );

        return new StockReportedActivity
        {
            PreviousHolderCount = combined.PreviousFilerCount,
            ReportedFilerCount = asFiled.CurrentFilerCount + combined.SoldOutFilerCount,
            NewFilerCount = combined.NewFilerCount,
            SoldOutFilerCount = combined.SoldOutFilerCount,
            // Net over reporters only: their new shares (zero for exits) minus their previous
            // shares (zero for new positions). Carried positions contribute nothing.
            NetReportedShareDelta = combinedShares - previousShares,
            CombinedHolderCount = combined.CurrentFilerCount,
            CombinedShares = combinedShares,
            CombinedValue = combined.CurrentValue,
        };
    }
}

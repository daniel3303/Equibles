using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.ViewModels.Holdings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class HoldingsActivityController : BaseController
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly CommonStockRepository _commonStockRepository;

    public HoldingsActivityController(
        InstitutionalHoldingRepository holdingRepository,
        CommonStockRepository commonStockRepository,
        ILogger<HoldingsActivityController> logger
    )
        : base(logger)
    {
        _holdingRepository = holdingRepository;
        _commonStockRepository = commonStockRepository;
    }

    [HttpGet("~/holdings/activity")]
    public async Task<IActionResult> Activity(DateOnly? date)
    {
        var reportDates = await _holdingRepository
            .GetAvailableReportDates()
            .OrderByDescending(d => d)
            .ToListAsync();

        var viewModel = new HoldingsActivityViewModel { AvailableDates = reportDates };
        if (reportDates.Count == 0)
            return View(viewModel);

        var requestedIndex = date.HasValue ? reportDates.IndexOf(date.Value) : -1;
        var selectedIndex = requestedIndex < 0 ? 0 : requestedIndex;
        var selectedDate = reportDates[selectedIndex];
        viewModel.SelectedDate = selectedDate;

        var previousDate =
            selectedIndex < reportDates.Count - 1
                ? reportDates[selectedIndex + 1]
                : (DateOnly?)null;
        viewModel.PreviousDate = previousDate;
        if (!previousDate.HasValue)
            return View(viewModel);

        // Pull the top N stocks by absolute Δ value in each direction. The cap is
        // applied server-side so the controller never materializes the full per-stock
        // aggregation for the whole universe.
        var movers = _holdingRepository
            .GetQuarterlyActivity(selectedDate, previousDate.Value)
            .Where(a => a.CurrentShares != a.PreviousShares);

        var topBuysAgg = await movers
            .Where(a => a.CurrentShares > a.PreviousShares)
            .OrderByDescending(a => a.CurrentValue - a.PreviousValue)
            .Take(HoldingsActivityViewModel.RowCap)
            .ToListAsync();
        var topSellsAgg = await movers
            .Where(a => a.CurrentShares < a.PreviousShares)
            .OrderBy(a => a.CurrentValue - a.PreviousValue)
            .Take(HoldingsActivityViewModel.RowCap)
            .ToListAsync();

        // New / Sold-out per-stock churn — runs as a separate aggregation because the
        // set-difference of (stock, holder) pairs across quarters can't live inside the
        // same GROUP BY as the share/value totals.
        var churn = _holdingRepository.GetQuarterlyNewSoldOutPositions(
            selectedDate,
            previousDate.Value
        );
        var newPositionsAgg = await churn
            .Where(c => c.NewFilerCount > 0)
            .OrderByDescending(c => c.NewFilerCount)
            .Take(HoldingsActivityViewModel.RowCap)
            .ToListAsync();
        var soldOutPositionsAgg = await churn
            .Where(c => c.SoldOutFilerCount > 0)
            .OrderByDescending(c => c.SoldOutFilerCount)
            .Take(HoldingsActivityViewModel.RowCap)
            .ToListAsync();

        var stockIds = topBuysAgg
            .Concat(topSellsAgg)
            .Select(a => a.CommonStockId)
            .Concat(newPositionsAgg.Select(c => c.CommonStockId))
            .Concat(soldOutPositionsAgg.Select(c => c.CommonStockId))
            .Distinct()
            .ToList();
        var stocks = await _commonStockRepository
            .GetAll()
            .Where(s => stockIds.Contains(s.Id))
            .Select(s => new StockLabel
            {
                Id = s.Id,
                Ticker = s.Ticker,
                Name = s.Name,
            })
            .ToDictionaryAsync(s => s.Id);

        viewModel.TopBuys = topBuysAgg.Select(a => MapRow(a, stocks)).ToList();
        viewModel.TopSells = topSellsAgg.Select(a => MapRow(a, stocks)).ToList();
        viewModel.NewPositions = newPositionsAgg.Select(c => MapChurnRow(c, stocks)).ToList();
        viewModel.SoldOutPositions = soldOutPositionsAgg
            .Select(c => MapChurnRow(c, stocks))
            .ToList();

        return View(viewModel);
    }

    private static HoldingsActivityRow MapChurnRow(
        Equibles.Holdings.Repositories.Models.MarketWideStockChurn churn,
        IDictionary<Guid, StockLabel> stocks
    )
    {
        stocks.TryGetValue(churn.CommonStockId, out var stock);
        return new HoldingsActivityRow
        {
            CommonStockId = churn.CommonStockId,
            Ticker = stock?.Ticker ?? "—",
            Name = stock?.Name ?? "Unknown",
            NewFilerCount = churn.NewFilerCount,
            SoldOutFilerCount = churn.SoldOutFilerCount,
        };
    }

    private static HoldingsActivityRow MapRow(
        Equibles.Holdings.Repositories.Models.MarketWideStockActivity activity,
        IDictionary<Guid, StockLabel> stocks
    )
    {
        stocks.TryGetValue(activity.CommonStockId, out var stock);
        return new HoldingsActivityRow
        {
            CommonStockId = activity.CommonStockId,
            Ticker = stock?.Ticker ?? "—",
            Name = stock?.Name ?? "Unknown",
            DeltaShares = activity.DeltaShares,
            DeltaValue = activity.DeltaValue,
            CurrentFilerCount = activity.CurrentFilerCount,
            PreviousFilerCount = activity.PreviousFilerCount,
        };
    }

    private class StockLabel
    {
        public Guid Id { get; set; }
        public string Ticker { get; set; }
        public string Name { get; set; }
    }
}

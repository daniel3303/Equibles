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
        var reportDates = await LoadAvailableReportDates();

        var viewModel = new HoldingsActivityViewModel { AvailableDates = reportDates };
        if (reportDates.Count == 0)
            return View(viewModel);

        var (selectedDate, previousDate) = ResolveSelectedAndPriorDate(date, reportDates);
        viewModel.SelectedDate = selectedDate;
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
        var stocks = await LoadStockLabels(stockIds);

        viewModel.TopBuys = topBuysAgg.Select(a => MapRow(a, stocks)).ToList();
        viewModel.TopSells = topSellsAgg.Select(a => MapRow(a, stocks)).ToList();
        viewModel.NewPositions = newPositionsAgg.Select(c => MapChurnRow(c, stocks)).ToList();
        viewModel.SoldOutPositions = soldOutPositionsAgg
            .Select(c => MapChurnRow(c, stocks))
            .ToList();

        return View(viewModel);
    }

    [HttpGet("~/holdings/filings")]
    public async Task<IActionResult> LatestFilings(int page = 1)
    {
        if (page < 1)
            page = 1;

        var query = _holdingRepository.GetRecentFilings().OrderByDescending(f => f.ImportedAt);

        var totalCount = await query.CountAsync();
        var skip = (page - 1) * LatestFilingsViewModel.PageSize;

        var filings = await query.Skip(skip).Take(LatestFilingsViewModel.PageSize).ToListAsync();

        return View(
            new LatestFilingsViewModel
            {
                Filings = filings,
                Page = page,
                TotalCount = totalCount,
            }
        );
    }

    [HttpGet("~/Holdings/MostHeld")]
    public async Task<IActionResult> MostHeld(DateOnly? date, string sort, int page = 1)
    {
        var reportDates = await LoadAvailableReportDates();

        var normalizedSort = sort switch
        {
            HoldingsMostHeldViewModel.SortFilersDelta => HoldingsMostHeldViewModel.SortFilersDelta,
            HoldingsMostHeldViewModel.SortValue => HoldingsMostHeldViewModel.SortValue,
            _ => HoldingsMostHeldViewModel.SortFilers,
        };
        var viewModel = new HoldingsMostHeldViewModel
        {
            AvailableDates = reportDates,
            Sort = normalizedSort,
            Page = Math.Max(1, page),
        };
        if (reportDates.Count == 0)
            return View(viewModel);

        var (selectedDate, previousDate) = ResolveSelectedAndPriorDate(date, reportDates);
        viewModel.SelectedDate = selectedDate;
        viewModel.PreviousDate = previousDate;

        // GetMostHeld needs both args; when no prior quarter exists we pass the
        // same date — the previous-side columns end up mirroring current, and the
        // view renders delta cells as "—" because PreviousDate is null.
        var priorForRepo = previousDate ?? selectedDate;
        var rankingQuery = _holdingRepository.GetMostHeld(selectedDate, priorForRepo);

        viewModel.TotalRows = await rankingQuery.CountAsync();
        viewModel.TotalUniverseFilers = await _holdingRepository
            .GetUniqueFilerIds(selectedDate)
            .CountAsync();
        var skip = (viewModel.Page - 1) * HoldingsMostHeldViewModel.PageSize;

        var orderedQuery = normalizedSort switch
        {
            HoldingsMostHeldViewModel.SortFilersDelta => rankingQuery
                .OrderByDescending(a => a.CurrentFilerCount - a.PreviousFilerCount)
                .ThenByDescending(a => a.CurrentFilerCount),
            HoldingsMostHeldViewModel.SortValue => rankingQuery
                .OrderByDescending(a => a.CurrentValue)
                .ThenByDescending(a => a.CurrentFilerCount),
            _ => rankingQuery
                .OrderByDescending(a => a.CurrentFilerCount)
                .ThenByDescending(a => a.CurrentValue),
        };

        var pageRows = await orderedQuery
            .Skip(skip)
            .Take(HoldingsMostHeldViewModel.PageSize)
            .ToListAsync();

        var stockIds = pageRows.Select(r => r.CommonStockId).ToList();
        var stocks = await LoadStockLabels(stockIds);

        var universe = viewModel.TotalUniverseFilers;
        viewModel.Rows = pageRows
            .Select(r =>
            {
                var (ticker, name) = ResolveStockCells(stocks, r.CommonStockId);
                return new HoldingsMostHeldRow
                {
                    CommonStockId = r.CommonStockId,
                    Ticker = ticker,
                    Name = name,
                    CurrentFilerCount = r.CurrentFilerCount,
                    PreviousFilerCount = r.PreviousFilerCount,
                    CurrentValue = r.CurrentValue,
                    PreviousValue = r.PreviousValue,
                    PercentOfUniverse =
                        universe > 0 ? (double)r.CurrentFilerCount / universe * 100.0 : 0,
                };
            })
            .ToList();

        return View(viewModel);
    }

    private static HoldingsActivityRow MapChurnRow(
        Equibles.Holdings.Repositories.Models.MarketWideStockChurn churn,
        IDictionary<Guid, StockLabel> stocks
    )
    {
        var (ticker, name) = ResolveStockCells(stocks, churn.CommonStockId);
        return new HoldingsActivityRow
        {
            CommonStockId = churn.CommonStockId,
            Ticker = ticker,
            Name = name,
            NewFilerCount = churn.NewFilerCount,
            SoldOutFilerCount = churn.SoldOutFilerCount,
        };
    }

    private static HoldingsActivityRow MapRow(
        Equibles.Holdings.Repositories.Models.MarketWideStockActivity activity,
        IDictionary<Guid, StockLabel> stocks
    )
    {
        var (ticker, name) = ResolveStockCells(stocks, activity.CommonStockId);
        return new HoldingsActivityRow
        {
            CommonStockId = activity.CommonStockId,
            Ticker = ticker,
            Name = name,
            DeltaShares = activity.DeltaShares,
            DeltaValue = activity.DeltaValue,
            CurrentFilerCount = activity.CurrentFilerCount,
            PreviousFilerCount = activity.PreviousFilerCount,
        };
    }

    private Task<List<DateOnly>> LoadAvailableReportDates() =>
        _holdingRepository.GetAvailableReportDates().OrderByDescending(d => d).ToListAsync();

    private Task<Dictionary<Guid, StockLabel>> LoadStockLabels(List<Guid> stockIds) =>
        _commonStockRepository
            .GetAll()
            .Where(s => stockIds.Contains(s.Id))
            .Select(s => new StockLabel
            {
                Id = s.Id,
                Ticker = s.Ticker,
                Name = s.Name,
            })
            .ToDictionaryAsync(s => s.Id);

    private static (DateOnly Selected, DateOnly? Previous) ResolveSelectedAndPriorDate(
        DateOnly? requested,
        List<DateOnly> reportDates
    )
    {
        var requestedIndex = requested.HasValue ? reportDates.IndexOf(requested.Value) : -1;
        var selectedIndex = requestedIndex < 0 ? 0 : requestedIndex;
        var selected = reportDates[selectedIndex];
        var previous =
            selectedIndex < reportDates.Count - 1
                ? reportDates[selectedIndex + 1]
                : (DateOnly?)null;
        return (selected, previous);
    }

    private static (string Ticker, string Name) ResolveStockCells(
        IDictionary<Guid, StockLabel> stocks,
        Guid stockId
    )
    {
        stocks.TryGetValue(stockId, out var stock);
        return (stock?.Ticker ?? "—", stock?.Name ?? "Unknown");
    }

    private class StockLabel
    {
        public Guid Id { get; set; }
        public string Ticker { get; set; }
        public string Name { get; set; }
    }
}

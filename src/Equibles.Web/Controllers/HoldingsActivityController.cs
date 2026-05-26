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
    public async Task<IActionResult> Activity(DateOnly? date, bool combined = false)
    {
        var reportDates = await LoadAvailableReportDates();

        var viewModel = new HoldingsActivityViewModel { AvailableDates = reportDates };
        if (reportDates.Count == 0)
            return View(viewModel);

        var dateSelection = ResolveCombinedDateSelection(date, combined, reportDates);
        viewModel.IsCombinedAvailable = dateSelection.IsCombinedAvailable;
        viewModel.IsCombinedSelected = dateSelection.IsCombinedSelected;
        viewModel.SelectedDate = dateSelection.SelectedDate;
        viewModel.PreviousDate = dateSelection.PreviousDate;

        if (!viewModel.PreviousDate.HasValue)
            return View(viewModel);

        var selectedDate = viewModel.SelectedDate;
        var previousDate = viewModel.PreviousDate.Value;

        var movers = (
            viewModel.IsCombinedSelected
                ? _holdingRepository.GetQuarterlyActivityCombined(selectedDate, previousDate)
                : _holdingRepository.GetQuarterlyActivity(selectedDate, previousDate)
        ).Where(a => a.CurrentShares != a.PreviousShares);

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

        var churn = viewModel.IsCombinedSelected
            ? _holdingRepository.GetQuarterlyNewSoldOutPositionsCombined(selectedDate, previousDate)
            : _holdingRepository.GetQuarterlyNewSoldOutPositions(selectedDate, previousDate);
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

    [HttpGet("~/holdings/stats")]
    public async Task<IActionResult> Stats()
    {
        var snapshots = await _holdingRepository
            .GetAumByReportDate()
            .OrderByDescending(a => a.ReportDate)
            .ToListAsync();

        return View(new StatsDashboardViewModel { Snapshots = snapshots });
    }

    [HttpGet("~/holdings/double-down")]
    public async Task<IActionResult> DoubleDown(
        DateOnly? date,
        double? minPct,
        int page = 1,
        bool combined = false
    )
    {
        var reportDates = await LoadAvailableReportDates();

        var threshold = minPct ?? DoubleDownViewModel.DefaultMinPct;
        if (threshold < 0)
            threshold = 0;
        var viewModel = new DoubleDownViewModel
        {
            AvailableDates = reportDates,
            MinPctIncrease = threshold,
            Page = Math.Max(1, page),
        };
        if (reportDates.Count < 2)
            return View(viewModel);

        var dateSelection = ResolveCombinedDateSelection(date, combined, reportDates);
        viewModel.IsCombinedAvailable = dateSelection.IsCombinedAvailable;
        viewModel.IsCombinedSelected = dateSelection.IsCombinedSelected;
        viewModel.SelectedDate = dateSelection.SelectedDate;
        viewModel.PreviousDate = dateSelection.PreviousDate;

        if (!viewModel.PreviousDate.HasValue)
            return View(viewModel);

        var selectedDate = viewModel.SelectedDate;
        var previousDate = viewModel.PreviousDate.Value;

        var query = (
            viewModel.IsCombinedSelected
                ? _holdingRepository.GetDoubleDownPositionsCombined(
                    selectedDate,
                    previousDate,
                    threshold
                )
                : _holdingRepository.GetDoubleDownPositions(selectedDate, previousDate, threshold)
        ).OrderByDescending(p => (double)(p.CurrentShares - p.PreviousShares) / p.PreviousShares);

        viewModel.TotalCount = await query.CountAsync();
        var skip = (viewModel.Page - 1) * DoubleDownViewModel.PageSize;
        viewModel.Positions = await query
            .Skip(skip)
            .Take(DoubleDownViewModel.PageSize)
            .ToListAsync();

        return View(viewModel);
    }

    [HttpGet("~/holdings/trends")]
    public async Task<IActionResult> Trends()
    {
        var aumSnapshots = await _holdingRepository
            .GetAumByReportDate()
            .OrderBy(a => a.ReportDate)
            .ToListAsync();

        var sectorAllocations = await _holdingRepository
            .GetSectorAllocationByReportDate()
            .OrderBy(s => s.ReportDate)
            .ThenBy(s => s.SectorName)
            .ToListAsync();

        return View(
            new TrendChartsViewModel
            {
                AumSnapshots = aumSnapshots,
                SectorAllocations = sectorAllocations,
            }
        );
    }

    [HttpGet("~/Holdings/MostHeld")]
    public async Task<IActionResult> MostHeld(
        DateOnly? date,
        string sort,
        int page = 1,
        bool combined = false
    )
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

        var dateSelection = ResolveCombinedDateSelection(date, combined, reportDates);
        viewModel.IsCombinedAvailable = dateSelection.IsCombinedAvailable;
        viewModel.IsCombinedSelected = dateSelection.IsCombinedSelected;
        viewModel.SelectedDate = dateSelection.SelectedDate;
        viewModel.PreviousDate = dateSelection.PreviousDate;

        var selectedDate = viewModel.SelectedDate;
        var priorForRepo = viewModel.PreviousDate ?? selectedDate;

        var rankingQuery = viewModel.IsCombinedSelected
            ? _holdingRepository.GetMostHeldCombined(selectedDate, priorForRepo)
            : _holdingRepository.GetMostHeld(selectedDate, priorForRepo);

        viewModel.TotalRows = await rankingQuery.CountAsync();
        viewModel.TotalUniverseFilers = viewModel.IsCombinedSelected
            ? await _holdingRepository
                .GetUniqueFilerIdsCombined(selectedDate, priorForRepo)
                .CountAsync()
            : await _holdingRepository.GetUniqueFilerIds(selectedDate).CountAsync();
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

    [HttpGet("~/holdings/heatmap")]
    public async Task<IActionResult> HeatMap(DateOnly? date, bool combined = false)
    {
        var reportDates = await LoadAvailableReportDates();

        var viewModel = new HoldingsHeatMapViewModel { AvailableDates = reportDates };
        if (reportDates.Count < 2)
            return View(viewModel);

        var dateSelection = ResolveCombinedDateSelection(date, combined, reportDates);
        viewModel.IsCombinedAvailable = dateSelection.IsCombinedAvailable;
        viewModel.IsCombinedSelected = dateSelection.IsCombinedSelected;
        viewModel.SelectedDate = dateSelection.SelectedDate;
        viewModel.PreviousDate = dateSelection.PreviousDate;

        if (!viewModel.PreviousDate.HasValue)
            return View(viewModel);

        var selectedDate = viewModel.SelectedDate;
        var previousDate = viewModel.PreviousDate.Value;

        var totalFilers = viewModel.IsCombinedSelected
            ? await _holdingRepository
                .GetUniqueFilerIdsCombined(selectedDate, previousDate)
                .CountAsync()
            : await _holdingRepository.GetUniqueFilerIds(selectedDate).CountAsync();
        viewModel.TotalUniverseFilers = totalFilers;

        var activity = await (
            viewModel.IsCombinedSelected
                ? _holdingRepository.GetQuarterlyActivityCombined(selectedDate, previousDate)
                : _holdingRepository.GetQuarterlyActivity(selectedDate, previousDate)
        )
            .Where(a => a.CurrentFilerCount >= 3)
            .ToListAsync();

        var churnLookup = (
            await (
                viewModel.IsCombinedSelected
                    ? _holdingRepository.GetQuarterlyNewSoldOutPositionsCombined(
                        selectedDate,
                        previousDate
                    )
                    : _holdingRepository.GetQuarterlyNewSoldOutPositions(selectedDate, previousDate)
            ).ToListAsync()
        ).ToDictionary(c => c.CommonStockId);

        var stockIds = activity.Select(a => a.CommonStockId).ToList();
        var stocks = await LoadStockLabels(stockIds);

        var points = new List<HeatMapPoint>(activity.Count);
        foreach (var a in activity)
        {
            churnLookup.TryGetValue(a.CommonStockId, out var churn);
            var newFilers = churn?.NewFilerCount ?? 0;
            var soldOut = churn?.SoldOutFilerCount ?? 0;

            var netConviction =
                a.CurrentFilerCount > 0
                    ? (double)(newFilers - soldOut) / a.CurrentFilerCount * 100.0
                    : 0;
            var retention =
                a.PreviousFilerCount > 0
                    ? (1.0 - (double)soldOut / a.PreviousFilerCount) * 100.0
                    : 0;
            var universePct =
                totalFilers > 0 ? (double)a.CurrentFilerCount / totalFilers * 100.0 : 0;

            var score = netConviction + retention + universePct;

            var (ticker, name) = ResolveStockCells(stocks, a.CommonStockId);
            points.Add(
                new HeatMapPoint
                {
                    CommonStockId = a.CommonStockId,
                    Ticker = ticker,
                    Name = name,
                    CurrentFilerCount = a.CurrentFilerCount,
                    CurrentValue = a.CurrentValue,
                    ConvictionScore = Math.Round(score, 1),
                    NetConvictionPct = Math.Round(netConviction, 1),
                    RetentionPct = Math.Round(retention, 1),
                    UniversePct = Math.Round(universePct, 2),
                }
            );
        }

        viewModel.Points = points
            .OrderByDescending(p => p.ConvictionScore)
            .Take(HoldingsHeatMapViewModel.MaxPoints)
            .ToList();

        return View(viewModel);
    }

    private Task<List<DateOnly>> LoadAvailableReportDates() =>
        _holdingRepository.GetAvailableReportDates().ToListAsync();

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

    private static (
        bool IsCombinedAvailable,
        bool IsCombinedSelected,
        DateOnly SelectedDate,
        DateOnly? PreviousDate
    ) ResolveCombinedDateSelection(DateOnly? date, bool combined, List<DateOnly> reportDates)
    {
        var isCombinedAvailable =
            reportDates.Count >= 2 && CombinedQuarterHelper.IsFilingWindowOpen(reportDates[0]);

        if (combined && isCombinedAvailable)
            return (true, true, reportDates[0], reportDates[1]);

        var (sel, prev) = ResolveSelectedAndPriorDate(date, reportDates);
        return (isCombinedAvailable, false, sel, prev);
    }

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

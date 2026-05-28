using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.Holdings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class HoldingsActivityController : BaseController
{
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly AumQuarterlySnapshotRepository _aumSnapshotRepository;
    private readonly SectorQuarterlySnapshotRepository _sectorSnapshotRepository;

    public HoldingsActivityController(
        InstitutionalHoldingRepository holdingRepository,
        CommonStockRepository commonStockRepository,
        AumQuarterlySnapshotRepository aumSnapshotRepository,
        SectorQuarterlySnapshotRepository sectorSnapshotRepository,
        ILogger<HoldingsActivityController> logger
    )
        : base(logger)
    {
        _holdingRepository = holdingRepository;
        _commonStockRepository = commonStockRepository;
        _aumSnapshotRepository = aumSnapshotRepository;
        _sectorSnapshotRepository = sectorSnapshotRepository;
    }

    [HttpGet("~/holdings/activity")]
    public async Task<IActionResult> Activity(DateOnly? date, bool combined = false)
    {
        var reportDates = await LoadAvailableReportDates();

        var viewModel = new HoldingsActivityViewModel { AvailableDates = reportDates };
        if (reportDates.Count == 0)
            return View(viewModel);

        ApplyDateSelection(viewModel, date, combined);

        if (!viewModel.PreviousDate.HasValue)
            return View(viewModel);

        var selectedDate = viewModel.SelectedDate;
        var previousDate = viewModel.PreviousDate.Value;

        var movers = _holdingRepository
            .GetQuarterlyActivity(selectedDate, previousDate, viewModel.IsCombinedSelected)
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

        var churn = _holdingRepository.GetQuarterlyNewSoldOutPositions(
            selectedDate,
            previousDate,
            viewModel.IsCombinedSelected
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

    [HttpGet("~/holdings/latest-13f-filings")]
    public async Task<IActionResult> LatestFilings(int page = 1)
    {
        page = Pagination.ClampPage(page);

        var query = _holdingRepository.GetRecentFilings().OrderByDescending(f => f.ImportedAt);

        var totalCount = await query.CountAsync();

        var filings = await query.Page(page, LatestFilingsViewModel.PageSize).ToListAsync();

        return View(
            new LatestFilingsViewModel
            {
                Filings = filings,
                Page = page,
                TotalCount = totalCount,
            }
        );
    }

    [HttpGet("~/holdings/13f-statistics")]
    public async Task<IActionResult> Stats()
    {
        // Reads the per-quarter snapshot table that the worker rebuilds on
        // every 13F import (with a daily safety-net pass). The legacy live
        // multi-distinct GROUP BY on InstitutionalHoldings could not finish
        // inside the 30s Npgsql command timeout at production scale.
        var snapshots = await _aumSnapshotRepository
            .GetAll()
            .OrderByDescending(a => a.ReportDate)
            .ToListAsync();

        return View(new StatsDashboardViewModel { Snapshots = snapshots });
    }

    [HttpGet("~/holdings/double-down-report")]
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
            Page = Pagination.ClampPage(page),
        };
        if (reportDates.Count < 2)
            return View(viewModel);

        ApplyDateSelection(viewModel, date, combined);

        if (!viewModel.PreviousDate.HasValue)
            return View(viewModel);

        var selectedDate = viewModel.SelectedDate;
        var previousDate = viewModel.PreviousDate.Value;

        var query = _holdingRepository
            .GetDoubleDownPositions(
                selectedDate,
                previousDate,
                threshold,
                viewModel.IsCombinedSelected
            )
            .OrderByDescending(p =>
                (double)(p.CurrentShares - p.PreviousShares) / p.PreviousShares
            );

        viewModel.TotalCount = await query.CountAsync();
        viewModel.Positions = await query
            .Page(viewModel.Page, DoubleDownViewModel.PageSize)
            .ToListAsync();

        return View(viewModel);
    }

    [HttpGet("~/holdings/13f-trends")]
    public async Task<IActionResult> Trends()
    {
        // Same snapshot-table reads as /holdings/stats; the legacy live
        // aggregates timed out at production scale.
        var aumSnapshots = await _aumSnapshotRepository
            .GetAll()
            .OrderBy(a => a.ReportDate)
            .ToListAsync();

        var sectorAllocations = await _sectorSnapshotRepository
            .GetAll()
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

    [HttpGet("~/holdings/most-held")]
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
            Page = Pagination.ClampPage(page),
        };
        if (reportDates.Count == 0)
            return View(viewModel);

        ApplyDateSelection(viewModel, date, combined);

        var selectedDate = viewModel.SelectedDate;
        var priorForRepo = viewModel.PreviousDate ?? selectedDate;

        var rankingQuery = _holdingRepository.GetMostHeld(
            selectedDate,
            priorForRepo,
            viewModel.IsCombinedSelected
        );

        viewModel.TotalRows = await rankingQuery.CountAsync();
        viewModel.TotalUniverseFilers = await _holdingRepository
            .GetUniqueFilerIds(selectedDate, priorForRepo, viewModel.IsCombinedSelected)
            .CountAsync();

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
            .Page(viewModel.Page, HoldingsMostHeldViewModel.PageSize)
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

    [HttpGet("~/holdings/conviction-heat-map")]
    public async Task<IActionResult> HeatMap(DateOnly? date, bool combined = false)
    {
        var reportDates = await LoadAvailableReportDates();

        var viewModel = new HoldingsHeatMapViewModel { AvailableDates = reportDates };
        if (reportDates.Count < 2)
            return View(viewModel);

        ApplyDateSelection(viewModel, date, combined);

        if (!viewModel.PreviousDate.HasValue)
            return View(viewModel);

        var selectedDate = viewModel.SelectedDate;
        var previousDate = viewModel.PreviousDate.Value;

        var totalFilers = await _holdingRepository
            .GetUniqueFilerIds(selectedDate, previousDate, viewModel.IsCombinedSelected)
            .CountAsync();
        viewModel.TotalUniverseFilers = totalFilers;

        var activity = await _holdingRepository
            .GetQuarterlyActivity(selectedDate, previousDate, viewModel.IsCombinedSelected)
            .Where(a => a.CurrentFilerCount >= 3)
            .ToListAsync();

        var churnLookup = (
            await _holdingRepository
                .GetQuarterlyNewSoldOutPositions(
                    selectedDate,
                    previousDate,
                    viewModel.IsCombinedSelected
                )
                .ToListAsync()
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
            .GetByIds(stockIds)
            .Select(s => new StockLabel
            {
                Id = s.Id,
                Ticker = s.Ticker,
                Name = s.Name,
            })
            .ToDictionaryAsync(s => s.Id);

    private static void ApplyDateSelection(
        QuarterlySelectionViewModel viewModel,
        DateOnly? date,
        bool combined
    )
    {
        var selection = ResolveCombinedDateSelection(date, combined, viewModel.AvailableDates);
        viewModel.IsCombinedAvailable = selection.IsCombinedAvailable;
        viewModel.IsCombinedSelected = selection.IsCombinedSelected;
        viewModel.SelectedDate = selection.SelectedDate;
        viewModel.PreviousDate = selection.PreviousDate;
    }

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
        var selected = reportDates.ResolveSelectedDateOrFirst(requested);
        return (selected, reportDates.PreviousFrom(selected));
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

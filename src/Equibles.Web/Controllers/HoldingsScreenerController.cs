using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Holdings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class HoldingsScreenerController : BaseController
{
    public const int RowCap = 200;

    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly IndustryRepository _industryRepository;

    public HoldingsScreenerController(
        InstitutionalHoldingRepository holdingRepository,
        IndustryRepository industryRepository,
        ILogger<HoldingsScreenerController> logger
    )
        : base(logger)
    {
        _holdingRepository = holdingRepository;
        _industryRepository = industryRepository;
    }

    [HttpGet("~/holdings/screener")]
    public async Task<IActionResult> Screener(
        [FromQuery] ScreenerCriteriaViewModel filters = null,
        [FromQuery(Name = "date")] DateOnly? date = null,
        [FromQuery(Name = "compareDate")] DateOnly? compareDate = null
    )
    {
        filters ??= new ScreenerCriteriaViewModel();

        var (reportDates, selectedDate, comparisonDate) = await ResolveScreenerDates(
            date,
            compareDate
        );

        var industryOptions = await _industryRepository
            .GetAll()
            .OrderBy(i => i.Name)
            .Select(i => new ScreenerIndustryOption { Id = i.Id, Name = i.Name })
            .ToListAsync();

        var viewModel = new ScreenerViewModel
        {
            Filters = filters,
            AvailableDates = reportDates,
            IndustryOptions = industryOptions,
        };

        if (selectedDate is null)
        {
            // Need at least two quarters to compute deltas; surface a friendly message
            // rather than running the screener against missing data.
            viewModel.Reason =
                "At least two distinct 13F report dates are required to run the screener.";
            return View(viewModel);
        }

        viewModel.SelectedDate = selectedDate.Value;
        viewModel.ComparisonDate = comparisonDate.Value;

        var criteria = ToCriteria(filters);
        var rows = await _holdingRepository
            .Screen(criteria, selectedDate.Value, comparisonDate.Value)
            .OrderByDescending(r => r.CurrentValue)
            .Take(RowCap)
            .ToListAsync();

        viewModel.Rows = rows.Select(r => new ScreenerResultRow
            {
                CommonStockId = r.CommonStockId,
                Ticker = r.Ticker,
                Name = r.Name,
                IndustryName = r.IndustryName,
                CurrentFilerCount = r.CurrentFilerCount,
                PreviousFilerCount = r.PreviousFilerCount,
                DeltaFilerCount = r.DeltaFilerCount,
                CurrentValue = r.CurrentValue,
                PreviousValue = r.PreviousValue,
                DeltaValue = r.DeltaValue,
                NewFilerCount = r.NewFilerCount,
                SoldOutFilerCount = r.SoldOutFilerCount,
                PercentOfFloat = r.PercentOfFloat,
            })
            .ToList();
        viewModel.TruncatedToCap = rows.Count >= RowCap;

        return View(viewModel);
    }

    [HttpGet("~/holdings/screener/export.csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] ScreenerCriteriaViewModel filters = null,
        [FromQuery(Name = "date")] DateOnly? date = null,
        [FromQuery(Name = "compareDate")] DateOnly? compareDate = null
    )
    {
        filters ??= new ScreenerCriteriaViewModel();

        var (_, selectedDate, comparisonDate) = await ResolveScreenerDates(date, compareDate);
        if (selectedDate is null)
            return NotFound();

        var criteria = ToCriteria(filters);
        // CSV export bypasses the UI row cap — analysts who download a CSV expect every
        // matching row, not the first 200. Materializing the full result set here is fine:
        // the Screen query is already filtered server-side, so the row count is bounded by
        // the user's criteria rather than the universe.
        var rows = await _holdingRepository
            .Screen(criteria, selectedDate.Value, comparisonDate.Value)
            .OrderByDescending(r => r.CurrentValue)
            .ToListAsync();

        var csv = BuildCsv(rows);
        var filename =
            $"screener-{selectedDate.Value:yyyyMMdd}-vs-{comparisonDate.Value:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", filename);
    }

    internal static string BuildCsv(IReadOnlyList<ScreenerRow> rows)
    {
        string[] headers =
        [
            "Ticker",
            "Name",
            "Industry",
            "CurrentFilerCount",
            "PreviousFilerCount",
            "DeltaFilerCount",
            "CurrentValue",
            "PreviousValue",
            "DeltaValue",
            "NewFilerCount",
            "SoldOutFilerCount",
            "PercentOfFloat",
        ];

        var csvRows = rows.Select(r =>
            new[]
            {
                r.Ticker,
                r.Name,
                r.IndustryName,
                CsvExportService.Format(r.CurrentFilerCount),
                CsvExportService.Format(r.PreviousFilerCount),
                CsvExportService.Format(r.DeltaFilerCount),
                CsvExportService.Format(r.CurrentValue),
                CsvExportService.Format(r.PreviousValue),
                CsvExportService.Format(r.DeltaValue),
                CsvExportService.Format(r.NewFilerCount),
                CsvExportService.Format(r.SoldOutFilerCount),
                r.PercentOfFloat.HasValue
                    ? r.PercentOfFloat.Value.ToString("F4", CultureInfo.InvariantCulture)
                    : string.Empty,
            }
        );

        return CsvExportService.BuildCsv(headers, csvRows);
    }

    // Loads available report dates desc and resolves the selected/comparison dates from
    // the query string. Returns (dates, null, null) when fewer than two distinct report
    // dates exist so callers can branch into their own "insufficient data" response.
    private async Task<(
        List<DateOnly> ReportDates,
        DateOnly? Selected,
        DateOnly? Comparison
    )> ResolveScreenerDates(DateOnly? date, DateOnly? compareDate)
    {
        var reportDates = await _holdingRepository.GetAvailableReportDates().ToListAsync();
        if (reportDates.Count < 2)
            return (reportDates, null, null);
        var selected =
            date.HasValue && reportDates.Contains(date.Value) ? date.Value : reportDates[0];
        // The default comparison must track the selected date — picking a fixed
        // reportDates[1] collapses to selected==comparison when selected is the
        // second-latest, and to a *newer* comparison when selected is older.
        var selectedIndex = reportDates.IndexOf(selected);
        DateOnly? comparison =
            compareDate.HasValue && reportDates.Contains(compareDate.Value) ? compareDate.Value
            : selectedIndex < reportDates.Count - 1 ? reportDates[selectedIndex + 1]
            : null;
        if (comparison is null)
            return (reportDates, null, null);
        return (reportDates, selected, comparison.Value);
    }

    internal static ScreenerCriteria ToCriteria(ScreenerCriteriaViewModel filters) =>
        new()
        {
            MinFilerCount = filters.MinFilerCount,
            MaxFilerCount = filters.MaxFilerCount,
            MinDeltaFilerCount = filters.MinDeltaFilerCount,
            MaxDeltaFilerCount = filters.MaxDeltaFilerCount,
            MinTotalValue = filters.MinTotalValue,
            MaxTotalValue = filters.MaxTotalValue,
            MinDeltaValue = filters.MinDeltaValue,
            MaxDeltaValue = filters.MaxDeltaValue,
            MinPctFloat = filters.MinPctFloat,
            MaxPctFloat = filters.MaxPctFloat,
            MinNewPositions = filters.MinNewPositions,
            MinSoldOutPositions = filters.MinSoldOutPositions,
            IndustryIds = filters.IndustryIds ?? [],
        };
}

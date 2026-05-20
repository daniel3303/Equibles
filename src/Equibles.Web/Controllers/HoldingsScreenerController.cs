using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers.Abstract;
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

    [HttpGet("~/Holdings/Screener")]
    public async Task<IActionResult> Screener(
        [FromQuery] ScreenerCriteriaViewModel filters = null,
        [FromQuery(Name = "date")] DateOnly? date = null,
        [FromQuery(Name = "compareDate")] DateOnly? compareDate = null
    )
    {
        filters ??= new ScreenerCriteriaViewModel();

        var reportDates = await _holdingRepository
            .GetAvailableReportDates()
            .OrderByDescending(d => d)
            .ToListAsync();

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

        if (reportDates.Count < 2)
        {
            // Need at least two quarters to compute deltas; surface a friendly message
            // rather than running the screener against missing data.
            viewModel.Reason =
                "At least two distinct 13F report dates are required to run the screener.";
            return View(viewModel);
        }

        var selectedDate =
            date.HasValue && reportDates.Contains(date.Value) ? date.Value : reportDates[0];
        var comparisonDate =
            compareDate.HasValue && reportDates.Contains(compareDate.Value)
                ? compareDate.Value
                : reportDates[1];
        viewModel.SelectedDate = selectedDate;
        viewModel.ComparisonDate = comparisonDate;

        var criteria = ToCriteria(filters);
        var rows = await _holdingRepository
            .Screen(criteria, selectedDate, comparisonDate)
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

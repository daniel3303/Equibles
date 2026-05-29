using System.Globalization;
using Equibles.Finra.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.ShortVolume;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class ShortVolumeController : BaseController
{
    private readonly DailyShortVolumeRepository _shortVolumeRepository;

    public ShortVolumeController(
        DailyShortVolumeRepository shortVolumeRepository,
        ILogger<ShortVolumeController> logger
    )
        : base(logger)
    {
        _shortVolumeRepository = shortVolumeRepository;
    }

    // Market-wide "largest short volume" ranking for a single trading day (defaults to the
    // latest available), mirroring the GetLargestShortVolume MCP tool and the institutions
    // browser's list/filter/pager shape.
    [HttpGet("~/short-volume")]
    public async Task<IActionResult> Index(
        string date,
        ShortVolumeSort sort = ShortVolumeSort.ShortVolumeDescending,
        int page = 1
    )
    {
        ViewData["Title"] = "Largest Short Volume";

        page = Pagination.ClampPage(page);
        const int pageSize = 50;

        var latestDate = await _shortVolumeRepository.GetLatestDate().FirstOrDefaultAsync();
        if (latestDate == default)
        {
            return View(new ShortVolumeBrowserViewModel { Sort = sort, Page = page });
        }

        var availableDates = await _shortVolumeRepository
            .GetAll()
            .Select(d => d.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        // The day selector posts yyyy-MM-dd values; an unparseable or missing value falls
        // back to the latest available trading day.
        var selectedDate = latestDate;
        if (
            !string.IsNullOrWhiteSpace(date)
            && DateOnly.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed
            )
        )
        {
            selectedDate = parsed;
        }

        // Zero-volume rows carry no meaningful short percentage, so they're excluded — same
        // rule the GetLargestShortVolume MCP tool applies.
        var query = _shortVolumeRepository
            .GetByDate(selectedDate)
            .Include(d => d.CommonStock)
            .Where(d => d.TotalVolume > 0);

        var ordered = sort switch
        {
            ShortVolumeSort.ShortPercentDescending => query
                .OrderByDescending(d => (double)d.ShortVolume / d.TotalVolume)
                .ThenBy(d => d.CommonStock.Ticker),
            ShortVolumeSort.TotalVolumeDescending => query
                .OrderByDescending(d => d.TotalVolume)
                .ThenBy(d => d.CommonStock.Ticker),
            ShortVolumeSort.Ticker => query.OrderBy(d => d.CommonStock.Ticker),
            _ => query.OrderByDescending(d => d.ShortVolume).ThenBy(d => d.CommonStock.Ticker),
        };

        var totalCount = await ordered.CountAsync();

        var pageRows = await ordered
            .Page(page, pageSize)
            .Select(d => new ShortVolumeListItemViewModel
            {
                Ticker = d.CommonStock.Ticker,
                Name = d.CommonStock.Name,
                ShortVolume = d.ShortVolume,
                ShortExemptVolume = d.ShortExemptVolume,
                TotalVolume = d.TotalVolume,
                ShortPercent = (double)d.ShortVolume / d.TotalVolume * 100,
            })
            .ToListAsync();

        var viewModel = new ShortVolumeBrowserViewModel
        {
            Records = pageRows,
            SelectedDate = selectedDate,
            LatestDate = latestDate,
            AvailableDates = availableDates,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

        return View(viewModel);
    }
}

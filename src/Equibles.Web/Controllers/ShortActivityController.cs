using System.Globalization;
using Equibles.Finra.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.ShortActivity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class ShortActivityController : BaseController
{
    private readonly ShortInterestRepository _shortInterestRepository;

    public ShortActivityController(
        ShortInterestRepository shortInterestRepository,
        ILogger<ShortActivityController> logger
    )
        : base(logger)
    {
        _shortInterestRepository = shortInterestRepository;
    }

    // Market-wide "most shorted" leaderboard for a single FINRA bi-monthly settlement date
    // (defaulting to the latest available), mirroring the most-held holdings leaderboard and
    // the largest-short-volume page. Reads go straight through the repository.
    [HttpGet("~/most-shorted")]
    public async Task<IActionResult> MostShorted(
        string date,
        MostShortedSort sort = MostShortedSort.CurrentShortPositionDescending,
        int page = 1
    )
    {
        ViewData["Title"] = "Most Shorted";

        page = Pagination.ClampPage(page);
        const int pageSize = 50;

        var latestDate = await _shortInterestRepository
            .GetLatestSettlementDate()
            .FirstOrDefaultAsync();
        if (latestDate == default)
        {
            return View(new MostShortedBrowserViewModel { Sort = sort, Page = page });
        }

        var availableDates = await _shortInterestRepository
            .GetAllSettlementDates()
            .OrderByDescending(d => d)
            .ToListAsync();

        // The date selector posts yyyy-MM-dd values; an unparseable or missing value falls
        // back to the latest available settlement date.
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

        var query = _shortInterestRepository
            .GetBySettlementDate(selectedDate)
            .Include(s => s.CommonStock);

        // Null DaysToCover coalesces to 0 so it sorts last under descending order.
        var ordered = sort switch
        {
            MostShortedSort.ChangeDescending => query
                .OrderByDescending(s => s.ChangeInShortPosition)
                .ThenBy(s => s.CommonStock.Ticker),
            MostShortedSort.DaysToCoverDescending => query
                .OrderByDescending(s => s.DaysToCover ?? 0m)
                .ThenBy(s => s.CommonStock.Ticker),
            MostShortedSort.Ticker => query.OrderBy(s => s.CommonStock.Ticker),
            _ => query
                .OrderByDescending(s => s.CurrentShortPosition)
                .ThenBy(s => s.CommonStock.Ticker),
        };

        var totalCount = await ordered.CountAsync();

        var pageRows = await ordered
            .Page(page, pageSize)
            .Select(s => new MostShortedListItemViewModel
            {
                Ticker = s.CommonStock.Ticker,
                Name = s.CommonStock.Name,
                CurrentShortPosition = s.CurrentShortPosition,
                ChangeInShortPosition = s.ChangeInShortPosition,
                AverageDailyVolume = s.AverageDailyVolume,
                DaysToCover = s.DaysToCover,
            })
            .ToListAsync();

        var viewModel = new MostShortedBrowserViewModel
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

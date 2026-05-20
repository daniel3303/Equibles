using Equibles.Search;
using Equibles.Search.Abstractions;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.ViewModels.Search;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.Web.Controllers;

public class SearchController : BaseController
{
    // Top hits per group on the overview page (all categories visible side by side).
    private const int MaxPerProviderOverview = 6;

    // Hits per group when one category is selected — the user clicked "See all" / picked a
    // single category, so show enough to feel like a list rather than the overview cap.
    private const int MaxPerProviderFocused = 50;

    private readonly SearchAggregator _searchAggregator;

    public SearchController(SearchAggregator searchAggregator, ILogger<SearchController> logger)
        : base(logger)
    {
        _searchAggregator = searchAggregator;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string q,
        string category,
        SearchSort sort,
        DateOnly? dateFrom,
        DateOnly? dateTo
    )
    {
        ViewData["Title"] = "Search";
        return View(await BuildViewModel(q, category, sort, dateFrom, dateTo));
    }

    // Results-only fragment for instant (as-you-type) search. instant-search.js fetches this
    // and swaps it into the page; the markup is identical to Index's results region.
    [HttpGet]
    public async Task<IActionResult> Results(
        string q,
        string category,
        SearchSort sort,
        DateOnly? dateFrom,
        DateOnly? dateTo
    )
    {
        return PartialView("_Results", await BuildViewModel(q, category, sort, dateFrom, dateTo));
    }

    // Aggregator returns every non-empty group; the view filters for display so the category
    // chips can still list all matched categories even when one is selected.
    private async Task<GlobalSearchViewModel> BuildViewModel(
        string q,
        string category,
        SearchSort sort,
        DateOnly? dateFrom,
        DateOnly? dateTo
    )
    {
        var groups = await _searchAggregator.Search(
            q,
            ResolveMaxPerProvider(category),
            HttpContext.RequestAborted,
            sort,
            dateFrom,
            dateTo
        );

        return new GlobalSearchViewModel
        {
            Query = q,
            Groups = groups,
            ActiveCategory = category,
            SortBy = sort,
            DateFrom = dateFrom,
            DateTo = dateTo,
        };
    }

    private static int ResolveMaxPerProvider(string category) =>
        string.IsNullOrWhiteSpace(category) ? MaxPerProviderOverview : MaxPerProviderFocused;
}

using Equibles.Search;
using Equibles.Search.Abstractions;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.ViewModels.Search;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.Web.Controllers;

public class SearchController : BaseController
{
    // Top hits per group on the overview page; "see all" links go to the existing per-module pages.
    private const int MaxPerProvider = 6;

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

        // Aggregator returns every non-empty group; the view filters for display so the category
        // chips can still list all matched categories even when one is selected.
        var groups = await _searchAggregator.Search(
            q,
            MaxPerProvider,
            HttpContext.RequestAborted,
            sort,
            dateFrom,
            dateTo
        );

        return View(
            new GlobalSearchViewModel
            {
                Query = q,
                Groups = groups,
                ActiveCategory = category,
                SortBy = sort,
                DateFrom = dateFrom,
                DateTo = dateTo,
            }
        );
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
        var groups = await _searchAggregator.Search(
            q,
            MaxPerProvider,
            HttpContext.RequestAborted,
            sort,
            dateFrom,
            dateTo
        );

        return PartialView(
            "_Results",
            new GlobalSearchViewModel
            {
                Query = q,
                Groups = groups,
                ActiveCategory = category,
                SortBy = sort,
                DateFrom = dateFrom,
                DateTo = dateTo,
            }
        );
    }
}

using Equibles.Search;
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
    public async Task<IActionResult> Index(string q, string category)
    {
        ViewData["Title"] = "Search";

        // Aggregator returns every non-empty group; the view filters for display so the category
        // chips can still list all matched categories even when one is selected.
        var groups = await _searchAggregator.Search(q, MaxPerProvider, HttpContext.RequestAborted);

        return View(
            new GlobalSearchViewModel
            {
                Query = q,
                Groups = groups,
                ActiveCategory = category,
            }
        );
    }
}

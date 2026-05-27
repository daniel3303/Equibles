using Equibles.CommonStocks.Repositories;
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
    private readonly CommonStockRepository _commonStockRepository;

    public SearchController(
        SearchAggregator searchAggregator,
        CommonStockRepository commonStockRepository,
        ILogger<SearchController> logger
    )
        : base(logger)
    {
        _searchAggregator = searchAggregator;
        _commonStockRepository = commonStockRepository;
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
        // When the user submits a query that is an exact ticker, skip the results page and go
        // straight to that stock. Only on the unfiltered overview — a category-filtered search
        // (e.g. "Filings") is an explicit request to stay on the results page.
        if (string.IsNullOrWhiteSpace(category))
        {
            var stock = await ResolveExactTicker(q);
            if (stock != null)
                return RedirectToAction("Show", "Stocks", new { ticker = stock.Ticker });
        }

        ViewData["Title"] = "Search";
        return View(await BuildViewModel(q, category, sort, dateFrom, dateTo));
    }

    // Resolves a query to a stock only on an exact (case-insensitive) ticker match — primary or
    // secondary. Returns null for company-name or partial queries so they fall through to search.
    private async Task<Equibles.CommonStocks.Data.Models.CommonStock> ResolveExactTicker(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return null;

        return await _commonStockRepository.GetByTicker(q.Trim().ToUpperInvariant());
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

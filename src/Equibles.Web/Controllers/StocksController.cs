using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class StocksController : BaseController
{
    private readonly CommonStockRepository _commonStockRepository;
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly StockTabService _stockTabService;

    public StocksController(
        CommonStockRepository commonStockRepository,
        InstitutionalHolderRepository institutionalHolderRepository,
        DocumentRepository documentRepository,
        StockTabService stockTabService,
        ILogger<StocksController> logger
    )
        : base(logger)
    {
        _commonStockRepository = commonStockRepository;
        _institutionalHolderRepository = institutionalHolderRepository;
        _documentRepository = documentRepository;
        _stockTabService = stockTabService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string search,
        StockSort sort = StockSort.Ticker,
        double? minMarketCap = null,
        int page = 1
    )
    {
        ViewData["Title"] = "Stocks";

        // page is a client-supplied query value; a non-positive page would emit
        // Skip((page-1)*pageSize) = a negative OFFSET, which PostgreSQL rejects
        // (22023) and surfaces as HTTP 500. Clamp to the first page.
        if (page < 1)
            page = 1;

        const int pageSize = 50;
        var query = _commonStockRepository.Search(search);

        if (minMarketCap.HasValue)
            query = query.Where(s => s.MarketCapitalization >= minMarketCap.Value);

        // The later OrderBy replaces the repository's default Ticker ordering;
        // Ticker is the tie-breaker so paging stays stable on equal market caps.
        query = sort switch
        {
            StockSort.Name => query.OrderBy(s => s.Name).ThenBy(s => s.Ticker),
            StockSort.MarketCapDescending => query
                .OrderByDescending(s => s.MarketCapitalization)
                .ThenBy(s => s.Ticker),
            StockSort.MarketCapAscending => query
                .OrderBy(s => s.MarketCapitalization)
                .ThenBy(s => s.Ticker),
            _ => query.OrderBy(s => s.Ticker),
        };

        var totalCount = await query.CountAsync();

        var stocks = await query
            .Include(s => s.Industry)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StockListItemViewModel
            {
                Ticker = s.Ticker,
                Name = s.Name,
                Industry = s.Industry != null ? s.Industry.Name : null,
                MarketCapitalization = s.MarketCapitalization,
                Cusip = s.Cusip,
            })
            .ToListAsync();

        var viewModel = new StockBrowserViewModel
        {
            Stocks = stocks,
            Search = search,
            Sort = sort,
            MinMarketCap = minMarketCap,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

        return View(viewModel);
    }

    // Default stock page — redirects to Price tab
    [HttpGet("~/Stocks/{ticker}")]
    public IActionResult Show(string ticker)
    {
        return RedirectToAction(nameof(Price), new { ticker });
    }

    [HttpGet("~/Stocks/{ticker}/Price")]
    public async Task<IActionResult> Price(string ticker)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "price");
        ViewData["TabViewModel"] = await _stockTabService.LoadPriceTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/Holdings")]
    public async Task<IActionResult> Holdings(string ticker, DateOnly? date)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "holdings");
        ViewData["TabViewModel"] = await _stockTabService.LoadHoldingsTab(stock, date);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/ShortVolume")]
    public async Task<IActionResult> ShortVolume(string ticker)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "short-volume");
        ViewData["TabViewModel"] = await _stockTabService.LoadShortVolumeTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/ShortInterest")]
    public async Task<IActionResult> ShortInterest(string ticker)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "short-interest");
        ViewData["TabViewModel"] = await _stockTabService.LoadShortInterestTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/Ftd")]
    public async Task<IActionResult> Ftd(string ticker)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "ftd");
        ViewData["TabViewModel"] = await _stockTabService.LoadFtdTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/Documents")]
    public async Task<IActionResult> Documents(string ticker)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "documents");
        ViewData["TabViewModel"] = await _stockTabService.LoadDocumentsTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/InsiderTrading")]
    public async Task<IActionResult> InsiderTrading(string ticker)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "insider-trading");
        ViewData["TabViewModel"] = await _stockTabService.LoadInsiderTradingTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/CongressionalTrades")]
    public async Task<IActionResult> CongressionalTrades(string ticker)
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, "congressional-trades");
        ViewData["TabViewModel"] = await _stockTabService.LoadCongressionalTradesTab(stock);
        return View("Show", viewModel);
    }

    // Private helpers
    private async Task<Equibles.CommonStocks.Data.Models.CommonStock> LoadStock(string ticker)
    {
        return await _commonStockRepository.GetByTicker(ticker.ToUpper());
    }

    private StockDetailViewModel BuildStockViewModel(
        Equibles.CommonStocks.Data.Models.CommonStock stock,
        string activeTab
    )
    {
        var viewModel = new StockDetailViewModel { Stock = stock, ActiveTab = activeTab };

        ViewData["Title"] = $"{stock.Ticker} - {stock.Name}";
        ViewData["Description"] =
            $"{stock.Ticker} - {stock.Name}. View institutional holdings, short volume, short interest, and SEC filings for {stock.Ticker}.";
        return viewModel;
    }

    [HttpGet("~/Stocks/{ticker}/Documents/{id:guid}")]
    public async Task<IActionResult> ShowDocument(string ticker, Guid id)
    {
        var document = await _documentRepository.GetWithContent(id);
        if (document == null)
            return NotFound();

        if (!string.Equals(document.CommonStock.Ticker, ticker, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var content =
            document.Content?.FileContent?.Bytes != null
                ? Encoding.UTF8.GetString(document.Content.FileContent.Bytes)
                : string.Empty;

        var viewModel = new DocumentViewModel
        {
            Document = document,
            Content = content,
            Ticker = ticker.ToUpper(),
        };

        ViewData["Title"] = $"{document.DocumentType.DisplayName} - {ticker.ToUpper()}";
        return View(viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/Holders/{cik}")]
    public async Task<IActionResult> ShowHolder(string ticker, string cik)
    {
        var stock = await _commonStockRepository.GetByTicker(ticker.ToUpper());
        if (stock == null)
            return NotFound();

        var holder = await _institutionalHolderRepository.GetByCik(cik);
        if (holder == null)
            return NotFound();

        var viewModel = await _stockTabService.LoadHolderDetail(stock, holder);

        ViewData["Title"] = $"{holder.Name} - {ticker.ToUpper()}";
        return View(viewModel);
    }
}

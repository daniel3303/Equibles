using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Sec.FinancialFacts.Data.Enums;
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
    [HttpGet("~/stocks/{ticker}")]
    public IActionResult Show(string ticker)
    {
        return RedirectToAction(nameof(Price), new { ticker });
    }

    [HttpGet("~/stocks/{ticker}/price")]
    public Task<IActionResult> Price(string ticker) =>
        ShowStockTab(ticker, "price", async s => await _stockTabService.LoadPriceTab(s));

    [HttpGet("~/stocks/{ticker}/holdings")]
    public Task<IActionResult> Holdings(string ticker, DateOnly? date) =>
        ShowStockTab(
            ticker,
            "holdings",
            async s => await _stockTabService.LoadHoldingsTab(s, date)
        );

    [HttpGet("~/stocks/{ticker}/shortvolume")]
    public Task<IActionResult> ShortVolume(string ticker) =>
        ShowStockTab(
            ticker,
            "short-volume",
            async s => await _stockTabService.LoadShortVolumeTab(s)
        );

    [HttpGet("~/stocks/{ticker}/shortinterest")]
    public Task<IActionResult> ShortInterest(string ticker) =>
        ShowStockTab(
            ticker,
            "short-interest",
            async s => await _stockTabService.LoadShortInterestTab(s)
        );

    [HttpGet("~/stocks/{ticker}/ftd")]
    public Task<IActionResult> Ftd(string ticker) =>
        ShowStockTab(ticker, "ftd", async s => await _stockTabService.LoadFtdTab(s));

    [HttpGet("~/stocks/{ticker}/financials")]
    public Task<IActionResult> Financials(
        string ticker,
        FinancialStatementType statement = FinancialStatementType.IncomeStatement,
        int? year = null,
        SecFiscalPeriod? period = null
    ) =>
        ShowStockTab(
            ticker,
            "financials",
            async s => await _stockTabService.LoadFinancialsTab(s, statement, year, period)
        );

    [HttpGet("~/stocks/{ticker}/documents")]
    public Task<IActionResult> Documents(string ticker) =>
        ShowStockTab(ticker, "documents", async s => await _stockTabService.LoadDocumentsTab(s));

    [HttpGet("~/stocks/{ticker}/insidertrading")]
    public Task<IActionResult> InsiderTrading(string ticker) =>
        ShowStockTab(
            ticker,
            "insider-trading",
            async s => await _stockTabService.LoadInsiderTradingTab(s)
        );

    [HttpGet("~/stocks/{ticker}/congressionaltrades")]
    public Task<IActionResult> CongressionalTrades(string ticker) =>
        ShowStockTab(
            ticker,
            "congressional-trades",
            async s => await _stockTabService.LoadCongressionalTradesTab(s)
        );

    private async Task<IActionResult> ShowStockTab(
        string ticker,
        string activeTab,
        Func<Equibles.CommonStocks.Data.Models.CommonStock, Task<object>> loadTab
    )
    {
        var stock = await LoadStock(ticker);
        if (stock == null)
            return NotFound();

        var viewModel = BuildStockViewModel(stock, activeTab);
        ViewData["TabViewModel"] = await loadTab(stock);
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

    [HttpGet("~/stocks/{ticker}/documents/{id:guid}")]
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

    [HttpGet("~/stocks/{ticker}/holders/{cik}")]
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

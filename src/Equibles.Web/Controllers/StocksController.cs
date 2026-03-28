using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Finra.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.Stocks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class StocksController : BaseController {
    private readonly CommonStockRepository _commonStockRepository;
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly DailyShortVolumeRepository _dailyShortVolumeRepository;
    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;

    public StocksController(
        CommonStockRepository commonStockRepository,
        InstitutionalHoldingRepository institutionalHoldingRepository,
        InstitutionalHolderRepository institutionalHolderRepository,
        DailyShortVolumeRepository dailyShortVolumeRepository,
        ShortInterestRepository shortInterestRepository,
        FailToDeliverRepository failToDeliverRepository,
        DocumentRepository documentRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        ILogger<StocksController> logger
    ) : base(logger) {
        _commonStockRepository = commonStockRepository;
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _institutionalHolderRepository = institutionalHolderRepository;
        _dailyShortVolumeRepository = dailyShortVolumeRepository;
        _shortInterestRepository = shortInterestRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _documentRepository = documentRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string search, int page = 1) {
        ViewData["Title"] = "Stocks";

        const int pageSize = 50;
        var query = _commonStockRepository.Search(search);
        var totalCount = await query.CountAsync();

        var stocks = await query
            .Include(s => s.Industry)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StockListItemViewModel {
                Ticker = s.Ticker,
                Name = s.Name,
                Industry = s.Industry != null ? s.Industry.Name : null,
                MarketCapitalization = s.MarketCapitalization,
                Cusip = s.Cusip
            })
            .ToListAsync();

        var viewModel = new StockBrowserViewModel {
            Stocks = stocks,
            Search = search,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return View(viewModel);
    }

    // Default stock page — redirects to Holdings tab
    [HttpGet("~/Stocks/{ticker}")]
    public IActionResult Show(string ticker) {
        return RedirectToAction(nameof(Holdings), new { ticker });
    }

    [HttpGet("~/Stocks/{ticker}/Holdings")]
    public async Task<IActionResult> Holdings(string ticker, DateOnly? date) {
        var stock = await LoadStock(ticker);
        if (stock == null) return NotFound();

        var viewModel = BuildStockViewModel(stock, "holdings");
        ViewData["TabViewModel"] = await LoadHoldingsTab(stock, date);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/ShortVolume")]
    public async Task<IActionResult> ShortVolume(string ticker) {
        var stock = await LoadStock(ticker);
        if (stock == null) return NotFound();

        var viewModel = BuildStockViewModel(stock, "short-volume");
        ViewData["TabViewModel"] = await LoadShortVolumeTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/ShortInterest")]
    public async Task<IActionResult> ShortInterest(string ticker) {
        var stock = await LoadStock(ticker);
        if (stock == null) return NotFound();

        var viewModel = BuildStockViewModel(stock, "short-interest");
        ViewData["TabViewModel"] = await LoadShortInterestTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/Ftd")]
    public async Task<IActionResult> Ftd(string ticker) {
        var stock = await LoadStock(ticker);
        if (stock == null) return NotFound();

        var viewModel = BuildStockViewModel(stock, "ftd");
        ViewData["TabViewModel"] = await LoadFtdTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/Documents")]
    public async Task<IActionResult> Documents(string ticker) {
        var stock = await LoadStock(ticker);
        if (stock == null) return NotFound();

        var viewModel = BuildStockViewModel(stock, "documents");
        ViewData["TabViewModel"] = await LoadDocumentsTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/InsiderTrading")]
    public async Task<IActionResult> InsiderTrading(string ticker) {
        var stock = await LoadStock(ticker);
        if (stock == null) return NotFound();

        var viewModel = BuildStockViewModel(stock, "insider-trading");
        ViewData["TabViewModel"] = await LoadInsiderTradingTab(stock);
        return View("Show", viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/CongressionalTrades")]
    public async Task<IActionResult> CongressionalTrades(string ticker) {
        var stock = await LoadStock(ticker);
        if (stock == null) return NotFound();

        var viewModel = BuildStockViewModel(stock, "congressional-trades");
        ViewData["TabViewModel"] = await LoadCongressionalTradesTab(stock);
        return View("Show", viewModel);
    }

    // Private helpers
    private async Task<Equibles.CommonStocks.Data.Models.CommonStock> LoadStock(string ticker) {
        return await _commonStockRepository.GetByTicker(ticker.ToUpper());
    }

    private StockDetailViewModel BuildStockViewModel(
        Equibles.CommonStocks.Data.Models.CommonStock stock, string activeTab) {
        var viewModel = new StockDetailViewModel {
            Stock = stock,
            ActiveTab = activeTab
        };

        ViewData["Title"] = $"{stock.Ticker} - {stock.Name}";
        ViewData["Description"] = $"{stock.Ticker} - {stock.Name}. View institutional holdings, short volume, short interest, and SEC filings for {stock.Ticker}.";
        return viewModel;
    }

    private async Task<HoldingsTabViewModel> LoadHoldingsTab(Equibles.CommonStocks.Data.Models.CommonStock stock, DateOnly? date) {
        var reportDates = await _institutionalHoldingRepository.GetHistoryByStock(stock)
            .Select(h => h.ReportDate).Distinct().OrderByDescending(d => d).ToListAsync();

        var selectedDate = date ?? reportDates.FirstOrDefault();

        if (selectedDate != default) {
            var allHoldings = _institutionalHoldingRepository.GetByStock(stock, selectedDate);
            var totalHolderCount = await allHoldings.Select(h => h.InstitutionalHolderId).Distinct().CountAsync();
            var totalShares = await allHoldings.SumAsync(h => h.Shares);
            var totalValue = await allHoldings.SumAsync(h => h.Value);

            var holdings = await allHoldings
                .Include(h => h.InstitutionalHolder)
                .OrderByDescending(h => h.Value).Take(100).ToListAsync();

            // Fetch previous quarter's shares for change % calculation
            var previousSharesByHolder = new Dictionary<Guid, long>();
            var selectedIndex = reportDates.IndexOf(selectedDate);
            if (selectedIndex >= 0 && selectedIndex < reportDates.Count - 1) {
                var previousDate = reportDates[selectedIndex + 1];
                var holderIds = holdings.Select(h => h.InstitutionalHolderId).ToList();
                previousSharesByHolder = await _institutionalHoldingRepository.GetByStock(stock, previousDate)
                    .Where(h => holderIds.Contains(h.InstitutionalHolderId))
                    .GroupBy(h => h.InstitutionalHolderId)
                    .ToDictionaryAsync(g => g.Key, g => g.Sum(h => h.Shares));
            }

            return new HoldingsTabViewModel {
                Holdings = holdings, AvailableDates = reportDates, SelectedDate = selectedDate,
                Ticker = stock.Ticker, TotalValue = totalValue, TotalShares = totalShares,
                HolderCount = totalHolderCount, DisplayedCount = holdings.Count,
                PreviousSharesByHolder = previousSharesByHolder
            };
        }

        return new HoldingsTabViewModel { AvailableDates = reportDates, SelectedDate = selectedDate, Ticker = stock.Ticker };
    }

    private async Task<ShortVolumeTabViewModel> LoadShortVolumeTab(Equibles.CommonStocks.Data.Models.CommonStock stock) {
        var shortVolumes = await _dailyShortVolumeRepository.GetHistoryByStock(stock)
            .OrderByDescending(d => d.Date).Take(90).ToListAsync();
        return new ShortVolumeTabViewModel { ShortVolumes = shortVolumes.OrderBy(d => d.Date).ToList(), Ticker = stock.Ticker };
    }

    private async Task<ShortInterestTabViewModel> LoadShortInterestTab(Equibles.CommonStocks.Data.Models.CommonStock stock) {
        var shortInterests = await _shortInterestRepository.GetHistoryByStock(stock)
            .OrderByDescending(s => s.SettlementDate).Take(24).ToListAsync();
        return new ShortInterestTabViewModel { ShortInterests = shortInterests.OrderBy(s => s.SettlementDate).ToList(), Ticker = stock.Ticker };
    }

    private async Task<FtdTabViewModel> LoadFtdTab(Equibles.CommonStocks.Data.Models.CommonStock stock) {
        var ftds = await _failToDeliverRepository.GetByStock(stock)
            .OrderByDescending(f => f.SettlementDate).Take(90).ToListAsync();
        return new FtdTabViewModel { FailsToDeliver = ftds.OrderBy(f => f.SettlementDate).ToList(), Ticker = stock.Ticker };
    }

    private async Task<DocumentsTabViewModel> LoadDocumentsTab(Equibles.CommonStocks.Data.Models.CommonStock stock) {
        var documents = await _documentRepository.GetByCompany(stock)
            .OrderByDescending(d => d.ReportingDate).Take(100).ToListAsync();
        return new DocumentsTabViewModel { Documents = documents, Ticker = stock.Ticker };
    }

    private async Task<InsiderTradingTabViewModel> LoadInsiderTradingTab(Equibles.CommonStocks.Data.Models.CommonStock stock) {
        var transactions = await _insiderTransactionRepository.GetByStock(stock)
            .Include(t => t.InsiderOwner)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToListAsync();
        return new InsiderTradingTabViewModel { Transactions = transactions, Ticker = stock.Ticker };
    }

    private async Task<CongressionalTradesTabViewModel> LoadCongressionalTradesTab(Equibles.CommonStocks.Data.Models.CommonStock stock) {
        var trades = await _congressionalTradeRepository.GetByStock(stock)
            .Include(t => t.CongressMember)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToListAsync();
        return new CongressionalTradesTabViewModel { Trades = trades, Ticker = stock.Ticker };
    }

    [HttpGet("~/Stocks/{ticker}/Documents/{id:guid}")]
    public async Task<IActionResult> ShowDocument(string ticker, Guid id) {
        var document = await _documentRepository.GetWithContent(id);
        if (document == null) return NotFound();

        if (!string.Equals(document.CommonStock.Ticker, ticker, StringComparison.OrdinalIgnoreCase)) {
            return NotFound();
        }

        var content = document.Content?.FileContent?.Bytes != null
            ? Encoding.UTF8.GetString(document.Content.FileContent.Bytes)
            : string.Empty;

        var viewModel = new DocumentViewModel {
            Document = document,
            Content = content,
            Ticker = ticker.ToUpper()
        };

        ViewData["Title"] = $"{document.DocumentType.DisplayName} - {ticker.ToUpper()}";
        return View(viewModel);
    }

    [HttpGet("~/Stocks/{ticker}/Holders/{cik}")]
    public async Task<IActionResult> ShowHolder(string ticker, string cik) {
        var stock = await _commonStockRepository.GetByTicker(ticker.ToUpper());
        if (stock == null) return NotFound();

        var holder = await _institutionalHolderRepository.GetByCik(cik);
        if (holder == null) return NotFound();

        // Get this holder's holdings history for this stock
        var holdings = await _institutionalHoldingRepository.GetHistoryByStock(stock)
            .Where(h => h.InstitutionalHolderId == holder.Id)
            .OrderByDescending(h => h.ReportDate)
            .Take(50)
            .ToListAsync();

        var viewModel = new HolderDetailViewModel {
            Stock = stock,
            Holder = holder,
            Holdings = holdings
        };

        ViewData["Title"] = $"{holder.Name} - {ticker.ToUpper()}";
        return View(viewModel);
    }
}

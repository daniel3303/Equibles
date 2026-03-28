using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.ViewModels.Stocks;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

[Service]
public class StockTabService {
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly DailyShortVolumeRepository _dailyShortVolumeRepository;
    private readonly ShortInterestRepository _shortInterestRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;

    public StockTabService(
        InstitutionalHoldingRepository institutionalHoldingRepository,
        InstitutionalHolderRepository institutionalHolderRepository,
        DailyShortVolumeRepository dailyShortVolumeRepository,
        ShortInterestRepository shortInterestRepository,
        FailToDeliverRepository failToDeliverRepository,
        DocumentRepository documentRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressionalTradeRepository congressionalTradeRepository
    ) {
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _institutionalHolderRepository = institutionalHolderRepository;
        _dailyShortVolumeRepository = dailyShortVolumeRepository;
        _shortInterestRepository = shortInterestRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _documentRepository = documentRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
    }

    public async Task<HoldingsTabViewModel> LoadHoldingsTab(CommonStock stock, DateOnly? date) {
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

    public async Task<ShortVolumeTabViewModel> LoadShortVolumeTab(CommonStock stock) {
        var shortVolumes = await _dailyShortVolumeRepository.GetHistoryByStock(stock)
            .OrderByDescending(d => d.Date).Take(90).ToListAsync();
        return new ShortVolumeTabViewModel { ShortVolumes = shortVolumes.OrderBy(d => d.Date).ToList(), Ticker = stock.Ticker };
    }

    public async Task<ShortInterestTabViewModel> LoadShortInterestTab(CommonStock stock) {
        var shortInterests = await _shortInterestRepository.GetHistoryByStock(stock)
            .OrderByDescending(s => s.SettlementDate).Take(24).ToListAsync();
        return new ShortInterestTabViewModel { ShortInterests = shortInterests.OrderBy(s => s.SettlementDate).ToList(), Ticker = stock.Ticker };
    }

    public async Task<FtdTabViewModel> LoadFtdTab(CommonStock stock) {
        var ftds = await _failToDeliverRepository.GetByStock(stock)
            .OrderByDescending(f => f.SettlementDate).Take(90).ToListAsync();
        return new FtdTabViewModel { FailsToDeliver = ftds.OrderBy(f => f.SettlementDate).ToList(), Ticker = stock.Ticker };
    }

    public async Task<DocumentsTabViewModel> LoadDocumentsTab(CommonStock stock) {
        var documents = await _documentRepository.GetByCompany(stock)
            .OrderByDescending(d => d.ReportingDate).Take(100).ToListAsync();
        return new DocumentsTabViewModel { Documents = documents, Ticker = stock.Ticker };
    }

    public async Task<InsiderTradingTabViewModel> LoadInsiderTradingTab(CommonStock stock) {
        var transactions = await _insiderTransactionRepository.GetByStock(stock)
            .Include(t => t.InsiderOwner)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToListAsync();
        return new InsiderTradingTabViewModel { Transactions = transactions, Ticker = stock.Ticker };
    }

    public async Task<CongressionalTradesTabViewModel> LoadCongressionalTradesTab(CommonStock stock) {
        var trades = await _congressionalTradeRepository.GetByStock(stock)
            .Include(t => t.CongressMember)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .ToListAsync();
        return new CongressionalTradesTabViewModel { Trades = trades, Ticker = stock.Ticker };
    }

    public async Task<HolderDetailViewModel> LoadHolderDetail(CommonStock stock, InstitutionalHolder holder) {
        var holdings = await _institutionalHoldingRepository.GetHistoryByStock(stock)
            .Where(h => h.InstitutionalHolderId == holder.Id)
            .OrderByDescending(h => h.ReportDate)
            .Take(50)
            .ToListAsync();

        return new HolderDetailViewModel {
            Stock = stock,
            Holder = holder,
            Holdings = holdings
        };
    }
}

using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Fred.Repositories;
using Equibles.Yahoo.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

[Service]
public class DataCountService {
    private readonly CommonStockRepository _commonStockRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly FredObservationRepository _fredObservationRepository;
    private readonly DailyStockPriceRepository _dailyStockPriceRepository;

    public DataCountService(
        CommonStockRepository commonStockRepository,
        DocumentRepository documentRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        InstitutionalHoldingRepository institutionalHoldingRepository,
        FailToDeliverRepository failToDeliverRepository,
        FredObservationRepository fredObservationRepository,
        DailyStockPriceRepository dailyStockPriceRepository
    ) {
        _commonStockRepository = commonStockRepository;
        _documentRepository = documentRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _fredObservationRepository = fredObservationRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
    }

    public async Task<int> GetStockCount() =>
        await _commonStockRepository.GetAll().CountAsync();

    public async Task<int> GetDocumentCount() =>
        await _documentRepository.GetAll().CountAsync();

    public async Task<int> GetInsiderTransactionCount() =>
        await _insiderTransactionRepository.GetAll().CountAsync();

    public async Task<int> GetCongressionalTradeCount() =>
        await _congressionalTradeRepository.GetAll().CountAsync();

    public async Task<int> GetInstitutionalHoldingCount() =>
        await _institutionalHoldingRepository.GetAll().CountAsync();

    public async Task<int> GetFailToDeliverCount() =>
        await _failToDeliverRepository.GetAll().CountAsync();

    public async Task<int> GetFredObservationCount() =>
        await _fredObservationRepository.GetAll().CountAsync();

    public async Task<int> GetDailyStockPriceCount() =>
        await _dailyStockPriceRepository.GetAll().CountAsync();
}

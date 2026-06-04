using Equibles.Cboe.Repositories;
using Equibles.Cftc.Repositories;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Fred.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

[Service]
public class DataCountService
{
    private readonly CommonStockRepository _commonStockRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly InsiderTransactionRepository _insiderTransactionRepository;
    private readonly CongressionalTradeRepository _congressionalTradeRepository;
    private readonly InstitutionalHoldingRepository _institutionalHoldingRepository;
    private readonly FailToDeliverRepository _failToDeliverRepository;
    private readonly FredObservationRepository _fredObservationRepository;
    private readonly DailyStockPriceRepository _dailyStockPriceRepository;
    private readonly CftcPositionReportRepository _cftcPositionReportRepository;
    private readonly CboePutCallRatioRepository _cboePutCallRatioRepository;
    private readonly CboeVixDailyRepository _cboeVixDailyRepository;

    public DataCountService(
        CommonStockRepository commonStockRepository,
        DocumentRepository documentRepository,
        InsiderTransactionRepository insiderTransactionRepository,
        CongressionalTradeRepository congressionalTradeRepository,
        InstitutionalHoldingRepository institutionalHoldingRepository,
        FailToDeliverRepository failToDeliverRepository,
        FredObservationRepository fredObservationRepository,
        DailyStockPriceRepository dailyStockPriceRepository,
        CftcPositionReportRepository cftcPositionReportRepository,
        CboePutCallRatioRepository cboePutCallRatioRepository,
        CboeVixDailyRepository cboeVixDailyRepository
    )
    {
        _commonStockRepository = commonStockRepository;
        _documentRepository = documentRepository;
        _insiderTransactionRepository = insiderTransactionRepository;
        _congressionalTradeRepository = congressionalTradeRepository;
        _institutionalHoldingRepository = institutionalHoldingRepository;
        _failToDeliverRepository = failToDeliverRepository;
        _fredObservationRepository = fredObservationRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _cftcPositionReportRepository = cftcPositionReportRepository;
        _cboePutCallRatioRepository = cboePutCallRatioRepository;
        _cboeVixDailyRepository = cboeVixDailyRepository;
    }

    public Task<int> GetStockCount() => CountAll(_commonStockRepository);

    public Task<int> GetDocumentCount() => CountAll(_documentRepository);

    public Task<int> GetInsiderTransactionCount() => CountAll(_insiderTransactionRepository);

    public Task<int> GetCongressionalTradeCount() => CountAll(_congressionalTradeRepository);

    public Task<int> GetInstitutionalHoldingCount() => CountAll(_institutionalHoldingRepository);

    public Task<int> GetFailToDeliverCount() => CountAll(_failToDeliverRepository);

    public Task<int> GetFredObservationCount() => CountAll(_fredObservationRepository);

    public Task<int> GetDailyStockPriceCount() => CountAll(_dailyStockPriceRepository);

    public Task<int> GetCftcPositionReportCount() => CountAll(_cftcPositionReportRepository);

    public Task<int> GetCboePutCallRatioCount() => CountAll(_cboePutCallRatioRepository);

    public Task<int> GetCboeVixDailyCount() => CountAll(_cboeVixDailyRepository);

    private static Task<int> CountAll<T>(BaseRepository<T> repository)
        where T : class => repository.GetAll().CountAsync();
}

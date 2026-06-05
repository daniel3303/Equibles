using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Services;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the gating behind the Fund Operations (N-CEN) and Fund Holdings (NPORT)
/// tabs: <see cref="StockTabService.LoadFundTabAvailability"/> reports each tab as
/// available only when the stock actually has a filing of that type. Operating
/// companies file neither, so both flags must be false — otherwise the page shows
/// empty fund tabs for every non-fund stock.
/// </summary>
public class StockTabServiceLoadFundTabAvailabilityTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly StockTabService _service;

    public StockTabServiceLoadFundTabAvailabilityTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new FinancialFactsModuleConfiguration(),
            new FinraModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _service = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new Form144FilingRepository(_dbContext),
            new FormDFilingRepository(_dbContext),
            new NCenFilingRepository(_dbContext),
            new NportFilingRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext),
            new CommonStockRepository(_dbContext)
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task LoadFundTabAvailability_FundWithBothFilings_ReportsBothAvailable()
    {
        var fund = new CommonStock { Ticker = "PHD", Name = "Pioneer Floating Rate Fund" };
        _dbContext.Add(fund);
        _dbContext.Add(
            new NportFiling
            {
                CommonStockId = fund.Id,
                AccessionNumber = "0001-NPORT",
                FilingDate = new DateOnly(2026, 3, 31),
            }
        );
        _dbContext.Add(
            new NCenFiling
            {
                CommonStockId = fund.Id,
                AccessionNumber = "0001-NCEN",
                FilingDate = new DateOnly(2026, 1, 31),
            }
        );
        await _dbContext.SaveChangesAsync();

        var (hasFundHoldings, hasFundOperations) = await _service.LoadFundTabAvailability(fund);

        hasFundHoldings.Should().BeTrue();
        hasFundOperations.Should().BeTrue();
    }

    [Fact]
    public async Task LoadFundTabAvailability_OperatingCompanyWithNoFundFilings_ReportsNeitherAvailable()
    {
        var stock = new CommonStock { Ticker = "ARE", Name = "Alexandria Real Estate Equities" };
        _dbContext.Add(stock);
        await _dbContext.SaveChangesAsync();

        var (hasFundHoldings, hasFundOperations) = await _service.LoadFundTabAvailability(stock);

        hasFundHoldings.Should().BeFalse();
        hasFundOperations.Should().BeFalse();
    }

    [Fact]
    public async Task LoadFundTabAvailability_OnlyNportFiling_ReportsHoldingsAvailableButNotOperations()
    {
        var fund = new CommonStock { Ticker = "ABC", Name = "Holdings Only Fund" };
        _dbContext.Add(fund);
        _dbContext.Add(
            new NportFiling
            {
                CommonStockId = fund.Id,
                AccessionNumber = "0002-NPORT",
                FilingDate = new DateOnly(2026, 3, 31),
            }
        );
        await _dbContext.SaveChangesAsync();

        var (hasFundHoldings, hasFundOperations) = await _service.LoadFundTabAvailability(fund);

        hasFundHoldings.Should().BeTrue();
        hasFundOperations.Should().BeFalse();
    }

    [Fact]
    public async Task LoadFundTabAvailability_OnlyNCenFiling_ReportsOperationsAvailableButNotHoldings()
    {
        var fund = new CommonStock { Ticker = "XYZ", Name = "Operations Only Fund" };
        _dbContext.Add(fund);
        _dbContext.Add(
            new NCenFiling
            {
                CommonStockId = fund.Id,
                AccessionNumber = "0002-NCEN",
                FilingDate = new DateOnly(2026, 1, 31),
            }
        );
        await _dbContext.SaveChangesAsync();

        var (hasFundHoldings, hasFundOperations) = await _service.LoadFundTabAvailability(fund);

        hasFundHoldings.Should().BeFalse();
        hasFundOperations.Should().BeTrue();
    }
}

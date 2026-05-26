using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
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
/// Contract: LoadHoldingsCombinedTab is the side-by-side view that aggregates the
/// latest two report quarters. With fewer than 2 distinct ReportDates the combined
/// view cannot be assembled, so the method must return a model that signals
/// unavailability while still populating the AvailableDates list so the UI can fall
/// back to the per-quarter selector.
/// </summary>
public class StockTabServiceLoadHoldingsCombinedTabFewerThanTwoQuartersTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;
    private readonly StockTabService _sut;

    public StockTabServiceLoadHoldingsCombinedTabFewerThanTwoQuartersTests()
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

        _sut = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            new InstitutionalHolderRepository(_dbContext),
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext)
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task LoadHoldingsCombinedTab_SingleReportDate_MarksCombinedUnavailableAndExposesAvailableDates()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Set<CommonStock>().Add(stock);

        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "Vanguard",
            Cik = "0001234567",
        };
        _dbContext.Set<InstitutionalHolder>().Add(holder);

        var onlyReportDate = new DateOnly(2025, 3, 31);
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    CommonStockId = stock.Id,
                    InstitutionalHolderId = holder.Id,
                    FilingDate = new DateOnly(2025, 5, 15),
                    ReportDate = onlyReportDate,
                    Value = 600_000,
                    Shares = 11_000,
                    ShareType = ShareType.Shares,
                    TitleOfClass = "COM",
                    AccessionNumber = "0001-25-000099",
                }
            );
        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadHoldingsCombinedTab(stock);

        result.Ticker.Should().Be("AAPL");
        result.IsCombinedView.Should().BeTrue();
        result
            .IsCombinedAvailable.Should()
            .BeFalse("only one quarter exists — nothing to combine against");
        result.AvailableDates.Should().ContainSingle().Which.Should().Be(onlyReportDate);
        result.GroupedHolders.Should().BeEmpty();
    }
}

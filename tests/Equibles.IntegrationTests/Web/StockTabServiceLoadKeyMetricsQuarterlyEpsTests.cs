using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
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
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Web.Services;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: LoadKeyMetrics uses full-year (10-K) EPS only.
/// Quarterly EPS must not leak into the P/E calculation.
/// </summary>
public class StockTabServiceLoadKeyMetricsQuarterlyEpsTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;
    private readonly StockTabService _sut;

    public StockTabServiceLoadKeyMetricsQuarterlyEpsTests()
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
    public async Task LoadKeyMetrics_OnlyQuarterlyEps_ReturnsNullEpsAndPeRatio()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            MarketCapitalization = 3_200_000_000_000,
        };
        _dbContext.Set<CommonStock>().Add(stock);

        _dbContext
            .Set<DailyStockPrice>()
            .Add(
                new DailyStockPrice
                {
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 5, 23),
                    Open = 224m,
                    High = 228m,
                    Low = 223m,
                    Close = 227.80m,
                    AdjustedClose = 227.80m,
                    Volume = 60_000_000,
                }
            );

        var epsConcept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "EarningsPerShareDiluted",
            Label = "Earnings Per Share (Diluted)",
        };
        _dbContext.Set<FinancialConcept>().Add(epsConcept);

        // Seed only Q4 EPS — no FullYear period
        _dbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    CommonStockId = stock.Id,
                    FinancialConceptId = epsConcept.Id,
                    Unit = "USD/shares",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2025, 10, 1),
                    PeriodEnd = new DateOnly(2025, 12, 31),
                    Value = 2.18m,
                    FiscalYear = 2025,
                    FiscalPeriod = SecFiscalPeriod.Q4,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2026, 2, 1),
                    AccessionNumber = "0000320193-26-000099",
                }
            );

        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadKeyMetrics(stock);

        result.LatestClose.Should().Be(227.80m);
        result.MarketCapitalization.Should().Be(3_200_000_000_000);
        result.EpsDiluted.Should().BeNull("only full-year EPS should be used");
        result.PeRatio.Should().BeNull("P/E cannot be computed without full-year EPS");
    }
}

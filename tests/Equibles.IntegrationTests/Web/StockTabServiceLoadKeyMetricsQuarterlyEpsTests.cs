using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.CorporateActions.Data;
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
    private readonly EquiblesFinancialDbContext _dbContext;
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
            new CorporateActionsModuleConfiguration(),
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
            new Form144FilingRepository(_dbContext),
            new FormDFilingRepository(_dbContext),
            new NCenFilingRepository(_dbContext),
            new NportFilingRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new EquityDailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext),
            new EquityIssuerRepository(_dbContext)
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task LoadKeyMetrics_OnlyQuarterlyEps_ReturnsNullEpsAndPeRatio()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193",
            MarketCapitalization: 3_200_000_000_000
        );
        _dbContext.Set<EquityIssuer>().Add(stock);

        _dbContext
            .Set<EquityDailyStockPrice>()
            .Add(
                new EquityDailyStockPrice
                {
                    Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                        _dbContext,
                        stock,
                        null
                    ),
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
                    EquityIssuerId = stock.Id,
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

    // What a 10-K leaves in the annual bucket beside the year's own figure, and what a foreign
    // filer states: each of these once drove the header's EPS and P/E.
    [Theory]
    // The discrete fourth quarter, filed the same day under the annual stamp.
    [InlineData("2025-10-01", "2025-12-31", 2.10, "USD/shares", "TenK", 8.00, 25.00)]
    // A loss: the figure shows, a multiple does not.
    [InlineData(null, null, null, "USD/shares", "TenK", -8.00, null, -8.00)]
    // Earnings stated in another currency.
    [InlineData(null, null, null, "CNY/shares", "TenK", null, null, 15.00)]
    // Per ordinary share on a 20-F, beside a receipt's close.
    [InlineData(null, null, null, "USD/shares", "TwentyF", 8.00, null)]
    public async Task LoadKeyMetrics_AnnualBucketNoise_NeverDrivesTheEpsOrMultiple(
        string strayStart,
        string strayEnd,
        double? strayValue,
        string unit,
        string form,
        double? expectedEps,
        double? expectedPe,
        double annualValue = 8.00
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ANNL",
            Name: "Annual Corp",
            Cik: "0000999901",
            MarketCapitalization: 1_000_000_000
        );
        _dbContext.Set<EquityIssuer>().Add(stock);
        _dbContext
            .Set<EquityDailyStockPrice>()
            .Add(
                new EquityDailyStockPrice
                {
                    Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                        _dbContext,
                        stock,
                        null
                    ),
                    Date = new DateOnly(2026, 5, 23),
                    Open = 200m,
                    High = 200m,
                    Low = 200m,
                    Close = 200m,
                    AdjustedClose = 200m,
                    Volume = 1_000,
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
        var documentType = DocumentType.FromValue(form);
        FinancialFact Eps(DateOnly start, DateOnly end, decimal value) =>
            new()
            {
                EquityIssuerId = stock.Id,
                FinancialConceptId = epsConcept.Id,
                Unit = unit,
                PeriodType = FactPeriodType.Duration,
                PeriodStart = start,
                PeriodEnd = end,
                Value = value,
                FiscalYear = 2025,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Form = documentType,
                FiledDate = new DateOnly(2026, 2, 1),
                AccessionNumber = "0000999901-26-000001",
            };
        _dbContext
            .Set<FinancialFact>()
            .Add(Eps(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), (decimal)annualValue));
        if (strayStart != null)
            _dbContext
                .Set<FinancialFact>()
                .Add(
                    Eps(
                        DateOnly.Parse(strayStart),
                        DateOnly.Parse(strayEnd),
                        (decimal)strayValue.Value
                    )
                );
        await _dbContext.SaveChangesAsync();

        var result = await _sut.LoadKeyMetrics(stock);

        result.EpsDiluted.Should().Be((decimal?)expectedEps);
        result.PeRatio.Should().Be((decimal?)expectedPe);
    }

    // A 10-for-1 split after the 10-K: the filed EPS is per old share, the close per new share.
    [Fact]
    public async Task LoadKeyMetrics_SplitAfterTheAnnualFiling_RestatesEpsOntoTheClosesShareBasis()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "SPLT",
            Name: "Split Corp",
            Cik: "0000999902",
            MarketCapitalization: 1_000_000_000
        );
        _dbContext.Set<EquityIssuer>().Add(stock);
        _dbContext
            .Set<EquityDailyStockPrice>()
            .Add(
                new EquityDailyStockPrice
                {
                    Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                        _dbContext,
                        stock,
                        null
                    ),
                    Date = new DateOnly(2026, 5, 23),
                    Open = 200m,
                    High = 200m,
                    Low = 200m,
                    Close = 200m,
                    AdjustedClose = 200m,
                    Volume = 1_000,
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
        _dbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = epsConcept.Id,
                    Unit = "USD/shares",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2025, 1, 1),
                    PeriodEnd = new DateOnly(2025, 12, 31),
                    Value = 80m,
                    FiscalYear = 2025,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(2026, 2, 1),
                    AccessionNumber = "0000999902-26-000001",
                }
            );
        _dbContext
            .Set<Equibles.CorporateActions.Data.Models.StockSplit>()
            .Add(
                new Equibles.CorporateActions.Data.Models.StockSplit
                {
                    EquityIssuerId = stock.Id,
                    EquityListingId = stock.Presentation.Listing.Id,
                    PriceSeriesTicker = "SPLT",
                    EffectiveDate = new DateOnly(2026, 3, 2),
                    Numerator = 10,
                    Denominator = 1,
                }
            );
        await _dbContext.SaveChangesAsync();
        var sut = new StockTabService(
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
            new EquityDailyStockPriceRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            new FinancialConceptRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            stockSplitRepository: new Equibles.CorporateActions.Repositories.StockSplitRepository(
                _dbContext
            )
        );

        var result = await sut.LoadKeyMetrics(stock);

        result.EpsDiluted.Should().Be(8m);
        result.PeRatio.Should().Be(25m);
    }
}

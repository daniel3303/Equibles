using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Pins <c>YahooPriceImportService.SyncCompanyProfile</c>. Yahoo's assetProfile
/// payload feeds the Sector + Industry upsert and links the CommonStock to the
/// matching Industry row. Each fact runs end-to-end through the service's
/// scope-resolved repositories.
/// </summary>
public class YahooPriceImportServiceCompanyProfileTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;
    private readonly CommonStockRepository _stockRepo;
    private readonly IndustryRepository _industryRepo;
    private readonly SectorRepository _sectorRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly YahooPriceImportService _service;

    public YahooPriceImportServiceCompanyProfileTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _stockRepo = new CommonStockRepository(_dbContext);
        _industryRepo = new IndustryRepository(_dbContext);
        _sectorRepo = new SectorRepository(_dbContext);
        var priceRepo = new DailyStockPriceRepository(_dbContext);

        _yahooClient = Substitute.For<IYahooFinanceClient>();
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DailyStockPriceRepository), priceRepo),
            (typeof(CommonStockRepository), _stockRepo),
            (typeof(IndustryRepository), _industryRepo),
            (typeof(SectorRepository), _sectorRepo)
        );

        _service = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _yahooClient,
            new TickerMapService(scopeFactory),
            errorReporter,
            Options.Create(new WorkerOptions())
        );

        _yahooClient
            .GetHistoricalPrices(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns([]);
        _yahooClient.GetKeyStatistics(Arg.Any<string>()).Returns((KeyStatistics)null);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Import_FirstRun_CreatesSectorIndustryAndLinksToStock()
    {
        var stock = SeedStock("AAPL");
        _yahooClient
            .GetCompanyProfile("AAPL")
            .Returns(
                new CompanyProfile { Sector = "Technology", Industry = "Consumer Electronics" }
            );

        await _service.Import(CancellationToken.None);

        var sectors = _sectorRepo.GetAll().ToList();
        var industries = _industryRepo.GetAll().ToList();
        sectors.Should().ContainSingle().Which.Name.Should().Be("Technology");
        industries.Should().ContainSingle().Which.Name.Should().Be("Consumer Electronics");
        industries[0].SectorId.Should().Be(sectors[0].Id);

        var refreshed = _stockRepo.GetAll().Single(s => s.Id == stock.Id);
        refreshed.IndustryId.Should().Be(industries[0].Id);
    }

    [Fact]
    public async Task Import_SecondRun_DoesNotDuplicateSectorOrIndustry()
    {
        // First run with AAPL → seeds rows. Second run with MSFT in the same sector +
        // industry should reuse the existing rows.
        SeedStock("AAPL");
        _yahooClient
            .GetCompanyProfile("AAPL")
            .Returns(new CompanyProfile { Sector = "Technology", Industry = "Software" });
        await _service.Import(CancellationToken.None);

        SeedStock("MSFT");
        _yahooClient
            .GetCompanyProfile("MSFT")
            .Returns(new CompanyProfile { Sector = "Technology", Industry = "Software" });
        await _service.Import(CancellationToken.None);

        _sectorRepo.GetAll().Count().Should().Be(1);
        _industryRepo.GetAll().Count().Should().Be(1);
    }

    [Fact]
    public async Task Import_SectorNameCaseDiffers_TreatedAsSameRow()
    {
        // Yahoo has been observed to lower-case sectors occasionally for some issuers.
        // The upsert must be case-insensitive so the taxonomy stays canonical.
        SeedStock("STOCK1");
        _yahooClient
            .GetCompanyProfile("STOCK1")
            .Returns(new CompanyProfile { Sector = "ENERGY", Industry = "Oil & Gas E&P" });
        await _service.Import(CancellationToken.None);

        SeedStock("STOCK2");
        _yahooClient
            .GetCompanyProfile("STOCK2")
            .Returns(new CompanyProfile { Sector = "energy", Industry = "Oil & Gas Midstream" });
        await _service.Import(CancellationToken.None);

        _sectorRepo.GetAll().Count().Should().Be(1);
        _industryRepo.GetAll().Count().Should().Be(2);
    }

    [Fact]
    public async Task Import_NullProfile_LeavesStockIndustryUntouched()
    {
        var stock = SeedStock("UNKNOWN");
        _yahooClient.GetCompanyProfile("UNKNOWN").Returns((CompanyProfile)null);

        await _service.Import(CancellationToken.None);

        var refreshed = _stockRepo.GetAll().Single(s => s.Id == stock.Id);
        refreshed.IndustryId.Should().BeNull();
        _sectorRepo.GetAll().Should().BeEmpty();
        _industryRepo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Import_IndustryAlreadyExistsWithoutSector_BackfillsTheSectorLink()
    {
        // Existing industry rows that pre-date the Sector taxonomy: the worker must
        // populate their SectorId when a fresh profile carries the link.
        var sectorless = new Industry { Name = "Semiconductors" };
        _industryRepo.Add(sectorless);
        await _industryRepo.SaveChanges();

        SeedStock("NVDA");
        _yahooClient
            .GetCompanyProfile("NVDA")
            .Returns(new CompanyProfile { Sector = "Technology", Industry = "Semiconductors" });

        await _service.Import(CancellationToken.None);

        var refreshed = _industryRepo.GetAll().Single(i => i.Id == sectorless.Id);
        refreshed.SectorId.Should().NotBeNull();
        _sectorRepo.GetAll().Single().Name.Should().Be("Technology");
    }

    private CommonStock SeedStock(string ticker)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = $"{ticker} Inc.",
            Cik = $"CIK-{ticker}",
        };
        _stockRepo.Add(stock);
        _stockRepo.SaveChanges().GetAwaiter().GetResult();
        return stock;
    }
}

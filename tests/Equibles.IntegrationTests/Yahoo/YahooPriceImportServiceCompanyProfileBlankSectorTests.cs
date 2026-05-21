using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
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

namespace Equibles.IntegrationTests.Yahoo;

public class YahooPriceImportServiceCompanyProfileBlankSectorTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;
    private readonly CommonStockRepository _stockRepo;
    private readonly IndustryRepository _industryRepo;
    private readonly SectorRepository _sectorRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly YahooPriceImportService _service;

    public YahooPriceImportServiceCompanyProfileBlankSectorTests()
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
    public async Task Import_BlankSectorWithValidIndustry_CreatesUnlinkedIndustryAndLinksStock()
    {
        // SyncCompanyProfile's only short-circuit on the profile fields is
        // IsNullOrWhiteSpace(profile.Industry); the Sector path goes through
        // UpsertSectorIfPresent, whose name + body promise null on blank input.
        // The downstream UpsertIndustry accepts Guid? sectorId. So a Yahoo
        // payload with a valid Industry but a blank Sector (observed in practice
        // for thinly-covered issuers) must still create the Industry row (with
        // SectorId = null) and link the stock to it — never silently drop the
        // industry just because the sector was missing. A refactor that bailed
        // when sectorId was null, or that tightened the outer guard to also
        // require Sector, would re-introduce that drop and break taxonomy
        // backfill for every sector-less issuer.
        var stock = SeedStock("XYZ");
        _yahooClient
            .GetCompanyProfile("XYZ")
            .Returns(new CompanyProfile { Sector = "   ", Industry = "Specialty Retail" });

        await _service.Import(CancellationToken.None);

        var industries = _industryRepo.GetAll().ToList();
        industries.Should().ContainSingle();
        industries[0].Name.Should().Be("Specialty Retail");
        industries[0].SectorId.Should().BeNull();
        _sectorRepo.GetAll().Should().BeEmpty();

        var refreshed = _stockRepo.GetAll().Single(s => s.Id == stock.Id);
        refreshed.IndustryId.Should().Be(industries[0].Id);
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

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

namespace Equibles.IntegrationTests.Yahoo;

public class YahooPriceImportServiceCompanyProfilePreservesSectorTests : IDisposable
{
    private readonly EquiblesDbContext _dbContext;
    private readonly CommonStockRepository _stockRepo;
    private readonly IndustryRepository _industryRepo;
    private readonly SectorRepository _sectorRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly YahooPriceImportService _service;

    public YahooPriceImportServiceCompanyProfilePreservesSectorTests()
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
    public async Task Import_ExistingIndustryAlreadyLinkedToSector_DoesNotOverwriteWithDifferentYahooSector()
    {
        // Contract (per the SyncCompanyProfile code comment):
        // "Backfill the sector link if it was missing — newly-imported industries that
        // pre-dated the Sector taxonomy would otherwise stay unlinked."
        // The comment is "if it was missing", not "if it differs" — an Industry that is
        // already linked to Sector A must not have that link overwritten to Sector B
        // when Yahoo returns the same industry under a different sector.
        var sectorA = new Sector { Name = "Technology" };
        _sectorRepo.Add(sectorA);
        await _sectorRepo.SaveChanges();

        var industry = new Industry { Name = "Pharmaceuticals", SectorId = sectorA.Id };
        _industryRepo.Add(industry);
        await _industryRepo.SaveChanges();

        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "PFE",
            Name = "PFE Inc.",
            Cik = "CIK-PFE",
        };
        _stockRepo.Add(stock);
        await _stockRepo.SaveChanges();

        _yahooClient
            .GetCompanyProfile("PFE")
            .Returns(new CompanyProfile { Sector = "Healthcare", Industry = "Pharmaceuticals" });

        await _service.Import(CancellationToken.None);

        var refreshed = _industryRepo.GetAll().Single(i => i.Id == industry.Id);
        refreshed
            .SectorId.Should()
            .Be(
                sectorA.Id,
                "the comment promises backfill of a missing link only; an existing link must be preserved"
            );
    }
}

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
/// Sibling to <see cref="YahooPriceImportServiceCompanyProfileTests"/>'s
/// <c>Import_IndustryAlreadyExistsWithoutSector_BackfillsTheSectorLink</c>,
/// which pins the BACKFILL leg. This pins the NON-UPDATE leg: when an
/// industry already has a <c>SectorId</c>, the upsert leaves it intact
/// even if Yahoo classifies the same industry under a different sector.
/// The production code's WHY-comment documents this explicitly:
/// <i>"An already-linked industry keeps its existing sector even when Yahoo
/// classifies it differently."</i> A regression that simplified the guard
/// from <c>!existing.SectorId.HasValue</c> to <c>existing.SectorId != sectorId</c>
/// would silently re-map industries on every Yahoo reclassification.
/// </summary>
public class YahooPriceImportServiceKeepExistingSectorTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CommonStockRepository _stockRepo;
    private readonly IndustryRepository _industryRepo;
    private readonly SectorRepository _sectorRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly YahooPriceImportService _service;

    public YahooPriceImportServiceKeepExistingSectorTests()
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
    public async Task Import_IndustryAlreadyLinkedToSector_KeepsExistingSectorOnYahooReclassification()
    {
        // Seed an Industry already linked to a Technology sector.
        var techSector = new Sector { Name = "Technology" };
        _sectorRepo.Add(techSector);
        await _sectorRepo.SaveChanges();
        var industry = new Industry { Name = "Semiconductors", SectorId = techSector.Id };
        _industryRepo.Add(industry);
        await _industryRepo.SaveChanges();

        SeedStock("NVDA");
        // Yahoo now classifies "Semiconductors" under a DIFFERENT sector.
        _yahooClient
            .GetCompanyProfile("NVDA")
            .Returns(new CompanyProfile { Sector = "Healthcare", Industry = "Semiconductors" });

        await _service.Import(CancellationToken.None);

        var refreshed = _industryRepo.GetAll().Single(i => i.Id == industry.Id);
        refreshed.SectorId.Should().Be(techSector.Id);
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

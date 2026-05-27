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
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// Pins the contract from GH-1591: when the parent <see cref="CommonStock"/> row
/// disappears between the per-cycle ticker-map read and the per-batch price flush,
/// the batch must NOT be inserted. Without the guard, Postgres rejects the batch
/// with <c>FK_DailyStockPrice_CommonStock_CommonStockId</c>; EF Core in-memory
/// (used here) silently inserts an orphan row instead, so the assertion is on the
/// row count post-import rather than on a thrown exception.
/// </summary>
public class YahooPriceImportServiceMissingCommonStockTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly DailyStockPriceRepository _priceRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly YahooPriceImportService _sut;

    public YahooPriceImportServiceMissingCommonStockTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _priceRepo = new DailyStockPriceRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);

        _yahooClient = Substitute.For<IYahooFinanceClient>();
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DailyStockPriceRepository), _priceRepo),
            (typeof(CommonStockRepository), _stockRepo)
        );

        _sut = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _yahooClient,
            new TickerMapService(scopeFactory),
            errorReporter,
            Options.Create(new WorkerOptions())
        );

        _yahooClient.GetKeyStatistics(Arg.Any<string>()).Returns((KeyStatistics)null);
        _yahooClient.GetCompanyProfile(Arg.Any<string>()).Returns((CompanyProfile)null);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Import_CommonStockDeletedBeforeFlush_DoesNotInsertOrphanPrices()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "CIK-AAPL",
        };
        _stockRepo.Add(apple);
        await _stockRepo.SaveChanges();

        // Reproduces the GH-1591 race: CompanySync (or any external mutator)
        // removes the CommonStock between TickerMapService.Build and the per-batch
        // flush. The Yahoo client substitute runs after the ticker map already
        // captured apple.Id and before FlushPriceBatch — exactly the window where
        // the parent row can vanish in production.
        _yahooClient
            .GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(_ =>
            {
                _dbContext.Remove(apple);
                _dbContext.SaveChanges();
                return new List<HistoricalPrice>
                {
                    new()
                    {
                        Date = new DateOnly(2026, 3, 25),
                        Open = 178m,
                        High = 186m,
                        Low = 176m,
                        Close = 184m,
                        AdjustedClose = 183m,
                        Volume = 42_000_000,
                    },
                };
            });

        await _sut.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().BeEmpty();
    }
}

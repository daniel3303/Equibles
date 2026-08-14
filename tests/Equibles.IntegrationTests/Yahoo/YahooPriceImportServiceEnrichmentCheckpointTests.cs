using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.IntegrationTests.Yahoo;

[Collection(ParadeDbCollection.Name)]
public class YahooPriceImportServiceEnrichmentCheckpointTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly IYahooFinanceClient _yahooClient;

    public YahooPriceImportServiceEnrichmentCheckpointTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _yahooClient = Substitute.For<IYahooFinanceClient>();
        _yahooClient
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Import_NewServiceAndDbContextAfterFirstBatch_ResumesAtNextUnattemptedStock()
    {
        await SeedStocks(Stock("AAPL"), Stock("MSFT"));

        var (firstService, firstContext) = CreateService();
        using (firstContext)
        {
            await firstService.Import(CancellationToken.None);
            firstService.HasEnrichmentBacklog.Should().BeTrue();
        }

        var firstAttempts = await GetAttemptTimes();
        firstAttempts["AAPL"].Should().NotBeNull();
        firstAttempts["MSFT"].Should().BeNull();

        var (restartedService, restartedContext) = CreateService();
        using (restartedContext)
        {
            await restartedService.Import(CancellationToken.None);
            restartedService.HasEnrichmentBacklog.Should().BeFalse();
        }

        var restartedAttempts = await GetAttemptTimes();
        restartedAttempts.Values.Should().OnlyContain(attemptedAt => attemptedAt != null);
        await _yahooClient.Received(1).GetKeyStatistics("AAPL");
        await _yahooClient.Received(1).GetKeyStatistics("MSFT");
        await _yahooClient.Received(1).GetCompanyProfile("AAPL");
        await _yahooClient.Received(1).GetCompanyProfile("MSFT");
    }

    [Fact]
    public async Task Import_HttpEnrichmentFailure_StampsAttempt()
    {
        await SeedStocks(Stock("AAPL"));
        _yahooClient
            .GetKeyStatistics("AAPL")
            .ThrowsAsync(new HttpRequestException("Unsupported ticker"));

        var (service, context) = CreateService();
        using (context)
            await service.Import(CancellationToken.None);

        var attempts = await GetAttemptTimes();
        attempts["AAPL"].Should().NotBeNull();
    }

    [Fact]
    public async Task Import_GenericEnrichmentFailure_StampsAttempt()
    {
        await SeedStocks(Stock("AAPL"));
        _yahooClient
            .GetCompanyProfile("AAPL")
            .ThrowsAsync(new InvalidOperationException("Unexpected response"));

        var (service, context) = CreateService();
        using (context)
            await service.Import(CancellationToken.None);

        var attempts = await GetAttemptTimes();
        attempts["AAPL"].Should().NotBeNull();
    }

    [Fact]
    public async Task Import_ShutdownCancellation_DoesNotStampAttempt()
    {
        await SeedStocks(Stock("AAPL"));
        using var cancellation = new CancellationTokenSource();
        _yahooClient
            .GetKeyStatistics("AAPL")
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromException<KeyStatistics>(
                    new OperationCanceledException(cancellation.Token)
                );
            });

        var (service, context) = CreateService();
        using (context)
        {
            var act = () => service.Import(cancellation.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        var attempts = await GetAttemptTimes();
        attempts["AAPL"].Should().BeNull();
    }

    private async Task SeedStocks(params CommonStock[] stocks)
    {
        await using var context = _fixture.CreateDbContext();
        var repository = new CommonStockRepository(context);
        repository.AddRange(stocks);
        await repository.SaveChanges();
    }

    private async Task<Dictionary<string, DateTime?>> GetAttemptTimes()
    {
        await using var context = _fixture.CreateDbContext();
        return await context
            .Set<CommonStock>()
            .AsNoTracking()
            .ToDictionaryAsync(stock => stock.Ticker, stock => stock.YahooEnrichmentAttemptedAt);
    }

    private (YahooPriceImportService Service, EquiblesFinancialDbContext Context) CreateService()
    {
        var context = _fixture.CreateDbContext();
        var stockRepository = new CommonStockRepository(context);
        var priceRepository = new DailyStockPriceRepository(context);
        var splitRepository = new StockSplitRepository(context);
        var dividendRepository = new CashDividendRepository(context);
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), stockRepository),
            (typeof(DailyStockPriceRepository), priceRepository),
            (typeof(StockSplitRepository), splitRepository),
            (typeof(ISharesOutstandingProvider), Substitute.For<ISharesOutstandingProvider>()),
            (
                typeof(CorporateActionPriceReconciliationManager),
                new CorporateActionPriceReconciliationManager(
                    splitRepository,
                    dividendRepository,
                    stockRepository,
                    new CorporateActionPriceReconciliationCursorRepository(context)
                )
            ),
            (
                typeof(StockSplitCaptureManager),
                new StockSplitCaptureManager(splitRepository, stockRepository)
            ),
            (
                typeof(CashDividendCaptureManager),
                new CashDividendCaptureManager(dividendRepository, stockRepository)
            )
        );
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var service = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _yahooClient,
            new TickerMapService(scopeFactory),
            errorReporter,
            Options.Create(new WorkerOptions()),
            Options.Create(
                new YahooPriceScraperOptions
                {
                    EnrichmentIntervalHours = 24,
                    EnrichmentBatchSize = 1,
                }
            )
        );
        return (service, context);
    }

    private static CommonStock Stock(string ticker) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = $"CIK-{ticker}",
        };
}

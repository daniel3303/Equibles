using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.TestSupport;
using Equibles.Worker;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.HostedService;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Extensions;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins that US key statistics (the only writer of the stored market cap) are reachable without a
/// price pass. Gated behind the whole-universe US price pass, enrichment never ran while the worker
/// restarted more often than that pass took, and every stored market cap froze.
/// </summary>
public class YahooPriceImportServiceUsEnrichmentTests
{
    private sealed class Harness
    {
        private readonly DbContextOptions<EquiblesFinancialDbContext> _dbOptions;
        public IYahooFinanceClient Client { get; } = Substitute.For<IYahooFinanceClient>();
        public YahooPriceImportService Sut { get; }

        public Harness(int enrichmentBatchSize = 250)
        {
            _dbOptions = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
                .EnableServiceProviderCaching(false)
                .Options;
            var services = new ServiceCollection();
            services.AddScoped(_ => NewContext());
            services.AddScoped<EquityIssuerRepository>();
            services.AddScoped<EquityListingRepository>();
            services.AddScoped<EquityDailyStockPriceRepository>();
            var scopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();
            Client
                .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
                .Returns<Task<YahooChartData>>(_ =>
                    throw new InvalidOperationException("enrichment must not fetch prices")
                );
            Sut = new YahooPriceImportService(
                scopeFactory,
                Substitute.For<ILogger<YahooPriceImportService>>(),
                Client,
                new TickerMapService(scopeFactory),
                new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>()),
                Options.Create(new WorkerOptions()),
                Options.Create(
                    new YahooPriceScraperOptions { EnrichmentBatchSize = enrichmentBatchSize }
                )
            );
        }

        private EquiblesFinancialDbContext NewContext()
        {
            var context = new EquiblesFinancialDbContext(
                _dbOptions,
                new IModuleConfiguration[]
                {
                    new CommonStocksModuleConfiguration(),
                    new YahooModuleConfiguration(),
                }
            );
            context.Database.EnsureCreated();
            return context;
        }

        public void SeedUs(params string[] tickers)
        {
            using var seed = NewContext();
            foreach (var ticker in tickers)
                seed.Set<EquityIssuer>().Add(EquityIssuerSeed.Create(Ticker: ticker));
            seed.SaveChanges();
        }
    }

    [Fact]
    public async Task ImportUsEnrichment_RequestsKeyStatisticsWithoutAPricePass()
    {
        var harness = new Harness();
        harness.SeedUs("QSI");

        await harness.Sut.ImportUsEnrichment(CancellationToken.None);

        await harness.Client.Received(1).GetKeyStatistics("QSI");
        await harness
            .Client.DidNotReceive()
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
        harness.Sut.HasEnrichmentBacklog.Should().BeFalse();
    }

    [Fact]
    public async Task ImportUsEnrichment_BoundsTheBatch_AndReportsTheBacklog()
    {
        var harness = new Harness(enrichmentBatchSize: 1);
        harness.SeedUs("QSI", "AAPL");

        await harness.Sut.ImportUsEnrichment(CancellationToken.None);

        await harness.Client.Received(1).GetKeyStatistics(Arg.Any<string>());
        await harness.Client.Received(1).GetKeyStatistics("AAPL");
        harness.Sut.HasEnrichmentBacklog.Should().BeTrue();
    }

    [Fact]
    public void AddYahooWorker_RegistersTheUsEnrichmentWorker()
    {
        var services = new ServiceCollection();

        services.AddYahooWorker();

        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(YahooUsEnrichmentWorker)
            );
    }
}

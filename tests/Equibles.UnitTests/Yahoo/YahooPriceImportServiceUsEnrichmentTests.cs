using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.TestSupport;
using Equibles.Worker;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.HostedService;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Extensions;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins that US key statistics, the only writer of the stored market cap, land without a price
/// pass in front of them, so the refresh never depends on a whole-universe pass completing.
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
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var services = new ServiceCollection();
            services.AddScoped(_ => NewContext());
            services.AddScoped<EquityIssuerRepository>();
            services.AddScoped<EquityListingRepository>();
            services.AddScoped<EquityDailyStockPriceRepository>();
            services.AddScoped(_ => Substitute.For<ISharesOutstandingProvider>());
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

        public EquiblesFinancialDbContext NewContext()
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
    public async Task ImportUsEnrichment_RefreshesTheMarketCapWithoutAPricePass()
    {
        var harness = new Harness();
        harness.SeedUs("QSI");
        harness
            .Client.GetKeyStatistics("QSI")
            .Returns(
                new KeyStatistics
                {
                    SharesOutstanding = 218_883_912,
                    MarketCapitalization = 267_038_373,
                }
            );

        await harness.Sut.ImportUsEnrichment(CancellationToken.None);

        await harness
            .Client.DidNotReceive()
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
        await using var db = harness.NewContext();
        var listing = await db.Set<EquityListing>()
            .Include(l => l.Security)
            .SingleAsync(l => l.Ticker == "QSI");
        listing.Security.MarketCapitalization.Should().Be(267_038_373);
        listing.Security.SharesOutstanding.Should().Be(218_883_912);
        listing.YahooEnrichmentAttemptedAt.Should().NotBeNull();
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

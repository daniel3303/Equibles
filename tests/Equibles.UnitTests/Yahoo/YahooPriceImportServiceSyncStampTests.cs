using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.TestSupport;
using Equibles.Worker;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins that a venue listing's price sync attempt is stamped whatever the chart call did, including
/// when it threw. The stamp is what bounds a covered listing to one fetch per interval; without it on
/// the failure path a symbol the feed never serves refetches from the floor every cycle.
/// </summary>
public class YahooPriceImportServiceSyncStampTests
{
    private static readonly MethodInfo ImportPrices = typeof(YahooPriceImportService).GetMethod(
        "ImportPrices",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    private static readonly MethodInfo BuildCatalogPriceTargets =
        typeof(YahooPriceImportService).GetMethod(
            "BuildCatalogPriceTargets",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

    private sealed class Harness
    {
        public DbContextOptions<EquiblesFinancialDbContext> DbOptions { get; }
        public IYahooFinanceClient Client { get; } = Substitute.For<IYahooFinanceClient>();
        public YahooPriceImportService Sut { get; }

        public Harness()
        {
            DbOptions = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
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
            Sut = new YahooPriceImportService(
                scopeFactory,
                Substitute.For<ILogger<YahooPriceImportService>>(),
                Client,
                new TickerMapService(scopeFactory),
                new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>()),
                Options.Create(new WorkerOptions()),
                Options.Create(new YahooPriceScraperOptions())
            );
        }

        public EquiblesFinancialDbContext NewContext()
        {
            var context = new EquiblesFinancialDbContext(
                DbOptions,
                new IModuleConfiguration[]
                {
                    new CommonStocksModuleConfiguration(),
                    new YahooModuleConfiguration(),
                }
            );
            context.Database.EnsureCreated();
            return context;
        }

        public EquityIssuer SeedParis()
        {
            var issuer = EquityIssuerSeed.Create(
                Ticker: "AIR",
                Name: "Airbus SE",
                Isin: "NL0000235190",
                MarketCountryCode: "FR",
                MarketIdentifierCode: "XPAR",
                IdentityState: EquityIdentityState.Verified,
                TradingCurrency: "EUR",
                QuoteUnitMultiplier: 1m
            );
            using var seed = NewContext();
            seed.Set<EquityIssuer>().Add(issuer);
            seed.SaveChanges();
            return issuer;
        }

        public async Task<int> Run()
        {
            var targets = await (Task<List<PriceSeriesTarget>>)
                BuildCatalogPriceTargets.Invoke(Sut, [CancellationToken.None])!;
            targets.Should().ContainSingle(t => t.Ticker == "AIR");
            return await (Task<int>)ImportPrices.Invoke(Sut, [targets, CancellationToken.None])!;
        }

        public DateTime? Stamp(EquityIssuer issuer)
        {
            using var context = NewContext();
            var listingId = issuer.Securities.Single().Listings.Single().Id;
            return context
                .Set<EquityListing>()
                .Single(l => l.Id == listingId)
                .YahooPriceSyncAttemptedAt;
        }
    }

    [Fact]
    public async Task AChartCallThatThrows_StillStampsTheAttempt()
    {
        var harness = new Harness();
        var issuer = harness.SeedParis();
        harness
            .Client.GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns<Task<Equibles.Integrations.Yahoo.Models.YahooChartData>>(_ =>
                throw new HttpRequestException("404")
            );

        var inserted = await harness.Run();

        inserted.Should().Be(0);
        await harness
            .Client.Received(1)
            .GetChart("AIR.PA", Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
        harness.Stamp(issuer).Should().NotBeNull("an unserved symbol must not refetch every cycle");
    }

    [Fact]
    public async Task AnUnexpectedFailure_StillStampsTheAttempt()
    {
        var harness = new Harness();
        var issuer = harness.SeedParis();
        harness
            .Client.GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns<Task<Equibles.Integrations.Yahoo.Models.YahooChartData>>(_ =>
                throw new InvalidOperationException("malformed body")
            );

        await harness.Run();

        harness.Stamp(issuer).Should().NotBeNull();
    }
}

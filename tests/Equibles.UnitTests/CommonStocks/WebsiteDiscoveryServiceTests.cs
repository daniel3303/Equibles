using System.Net;
using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Configuration;
using Equibles.CommonStocks.HostedService.Services;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: <c>WebsiteDiscoveryService.Import</c> consults the registered
/// <c>IWebsiteSource</c>s in priority order, only hands later sources the stocks
/// earlier sources left unfilled, persists the first candidate that survives the
/// reachability probe, stamps definitive misses for the cooldown back-off, and
/// skips the stamp when a source errored so those stocks retry cleanly. Its
/// candidates are the current directory across markets: a verified venue issuer
/// is handed over with its venue identity, a legacy non-US presentation never is.
/// </summary>
public class WebsiteDiscoveryServiceTests
{
    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IServiceScopeFactory ScopeFactory(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<EquityIssuerRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static WebsiteDiscoveryService BuildSut(
        DbContextOptions<EquiblesFinancialDbContext> options,
        IEnumerable<IWebsiteSource> sources,
        HttpStatusCode probeStatus = HttpStatusCode.OK,
        int? batchSize = null,
        IBus bus = null
    )
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);
        var probe = new WebsiteProbeClient(
            new HttpClient(new FixedStatusHandler(probeStatus)),
            stealth,
            Substitute.For<ILogger<WebsiteProbeClient>>()
        );
        var discoveryOptions = new WebsiteDiscoveryOptions();
        if (batchSize.HasValue)
            discoveryOptions.BatchSize = batchSize.Value;
        return new WebsiteDiscoveryService(
            ScopeFactory(options),
            sources,
            probe,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<ILogger<WebsiteDiscoveryService>>(),
            Options.Create(discoveryOptions),
            bus ?? Substitute.For<IBus>()
        );
    }

    private static async Task<EquityIssuer> SeedVenueStock(
        DbContextOptions<EquiblesFinancialDbContext> options,
        string ticker,
        EquityIdentityState state,
        string lei = "R0MUWSFPU8MPRO8K5P83",
        string isin = "FR0000131104"
    )
    {
        using var ctx = NewContext(options);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: ticker + " SA",
            LegalEntityIdentifier: lei,
            Isin: isin,
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            IdentityState: state,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        ctx.Set<EquityIssuer>().Add(stock);
        await ctx.SaveChangesAsync();
        return stock;
    }

    private static async Task<EquityIssuer> SeedStock(
        DbContextOptions<EquiblesFinancialDbContext> options,
        string ticker,
        string website = null,
        DateTime? checkedAt = null,
        double marketCap = 0
    )
    {
        using var ctx = NewContext(options);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Cik: ticker.PadLeft(10, '0'),
            Name: ticker + " Inc",
            Website: website,
            WebsiteCheckedAt: checkedAt,
            MarketCapitalization: marketCap
        );
        ctx.Set<EquityIssuer>().Add(stock);
        await ctx.SaveChangesAsync();
        return stock;
    }

    private static async Task<EquityIssuer> Reload(
        DbContextOptions<EquiblesFinancialDbContext> options,
        Guid id
    )
    {
        using var ctx = NewContext(options);
        return await ctx.Set<EquityIssuer>().FirstAsync(s => s.Id == id);
    }

    [Fact]
    public async Task HigherPrioritySourceWins_AndLaterSourcesOnlySeeUnfilledStocks()
    {
        var options = NewDbOptions();
        EquityIssuer filled = await SeedStock(options, "AAA");
        EquityIssuer leftover = await SeedStock(options, "BBB");
        var primary = new StubSource(
            priority: 10,
            answers: new Dictionary<string, string> { ["AAA"] = "www.aaa.com" }
        );
        var fallback = new StubSource(
            priority: 20,
            answers: new Dictionary<string, string> { ["BBB"] = "www.bbb.com" }
        );

        await BuildSut(options, [fallback, primary]).Import(CancellationToken.None);

        (await Reload(options, filled.Id)).Website.Should().Be("https://www.aaa.com");
        (await Reload(options, leftover.Id)).Website.Should().Be("https://www.bbb.com");
        primary.SeenTickers.Should().BeEquivalentTo(["AAA", "BBB"]);
        fallback
            .SeenTickers.Should()
            .BeEquivalentTo(["BBB"], "AAA was already filled by the primary source");
    }

    [Fact]
    public async Task DefinitiveMissAcrossAllSources_StampsTheAttempt()
    {
        var options = NewDbOptions();
        EquityIssuer stock = await SeedStock(options, "AAA");

        await BuildSut(options, [new StubSource(10, [])]).Import(CancellationToken.None);

        EquityIssuer reloaded = await Reload(options, stock.Id);
        reloaded.Website.Should().BeNull();
        reloaded.WebsiteCheckedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FailedProbe_FallsThroughToNextSource()
    {
        var options = NewDbOptions();
        EquityIssuer stock = await SeedStock(options, "AAA");
        var primary = new StubSource(
            10,
            new Dictionary<string, string> { ["AAA"] = "www.dead-host.com" }
        );

        await BuildSut(options, [primary], HttpStatusCode.NotFound).Import(CancellationToken.None);

        EquityIssuer reloaded = await Reload(options, stock.Id);
        reloaded.Website.Should().BeNull("the only candidate failed the reachability probe");
        reloaded
            .WebsiteCheckedAt.Should()
            .NotBeNull("a probed-and-dead candidate is a definitive miss");
    }

    [Fact]
    public async Task ThrowingSource_DoesNotBlockOthers_AndSkipsTheMissStamp()
    {
        var options = NewDbOptions();
        EquityIssuer found = await SeedStock(options, "AAA");
        EquityIssuer missed = await SeedStock(options, "BBB");
        var broken = new ThrowingSource(priority: 10);
        var working = new StubSource(
            20,
            new Dictionary<string, string> { ["AAA"] = "www.aaa.com" }
        );

        await BuildSut(options, [broken, working]).Import(CancellationToken.None);

        (await Reload(options, found.Id)).Website.Should().Be("https://www.aaa.com");
        EquityIssuer reloadedMiss = await Reload(options, missed.Id);
        reloadedMiss.Website.Should().BeNull();
        reloadedMiss
            .WebsiteCheckedAt.Should()
            .BeNull("a stock unanswered while a source errored deserves a clean retry next cycle");
    }

    [Fact]
    public async Task StocksInsideCooldown_AreNotAttempted()
    {
        var options = NewDbOptions();
        await SeedStock(options, "AAA", checkedAt: DateTime.UtcNow.AddDays(-1));
        EquityIssuer eligible = await SeedStock(
            options,
            "BBB",
            checkedAt: DateTime.UtcNow.AddDays(-90)
        );
        var source = new StubSource(10, new Dictionary<string, string> { ["BBB"] = "www.bbb.com" });

        await BuildSut(options, [source]).Import(CancellationToken.None);

        source
            .SeenTickers.Should()
            .BeEquivalentTo(["BBB"], "AAA was attempted within the cooldown window");
        (await Reload(options, eligible.Id)).Website.Should().Be("https://www.bbb.com");
    }

    [Fact]
    public async Task FilledStocks_AreNeverCandidates()
    {
        var options = NewDbOptions();
        await SeedStock(options, "AAA", website: "https://www.already.com");
        var source = new StubSource(10, []);

        await BuildSut(options, [source]).Import(CancellationToken.None);

        source.SeenTickers.Should().BeEmpty();
    }

    [Fact]
    public async Task LargestMarketCapStocks_AreAttemptedFirst()
    {
        var options = NewDbOptions();
        // A bounded batch must drain the high-value companies before obscure ones, regardless of
        // ticker — a single bad bulk run can leave thousands pending and alphabetical order would
        // bury the large caps behind them.
        await SeedStock(options, "AAA", marketCap: 1_000_000);
        await SeedStock(options, "ZZZ", marketCap: 1_000_000_000);
        await SeedStock(options, "MMM", marketCap: 50_000_000);
        var source = new StubSource(10, []);

        await BuildSut(options, [source], batchSize: 2).Import(CancellationToken.None);

        source
            .SeenTickers.Should()
            .BeEquivalentTo(
                ["ZZZ", "MMM"],
                "the two largest caps are attempted first; the tiny AAA waits for the next cycle"
            );
    }

    [Fact]
    public async Task VerifiedVenueIssuer_IsACandidate_WithItsVenueIdentity_AndTheEventNamesItsVenue()
    {
        var options = NewDbOptions();
        EquityIssuer paris = await SeedVenueStock(options, "BNP", EquityIdentityState.Verified);
        EquityIssuer nyse = await SeedStock(options, "BNP");
        var source = new StubSource(
            10,
            new Dictionary<string, string> { ["BNP"] = "www.example-bank.com" }
        );
        var bus = Substitute.For<IBus>();

        await BuildSut(options, [source], bus: bus).Import(CancellationToken.None);

        var venueStock = source.SeenStocks.Should().ContainSingle(s => s.Id == paris.Id).Subject;
        venueStock.Cik.Should().BeNull();
        venueStock.LegalEntityIdentifier.Should().Be("R0MUWSFPU8MPRO8K5P83");
        venueStock.Isin.Should().Be("FR0000131104");
        venueStock.MarketIdentifierCode.Should().Be("XPAR");
        venueStock.MarketCountryCode.Should().Be("FR");
        venueStock.Symbol.Should().Be("XPAR:BNP");
        var usStock = source.SeenStocks.Should().ContainSingle(s => s.Id == nyse.Id).Subject;
        usStock.MarketCountryCode.Should().Be("US");
        usStock.MarketIdentifierCode.Should().BeNull();
        usStock.Symbol.Should().Be("BNP");

        (await Reload(options, paris.Id)).Website.Should().Be("https://www.example-bank.com");
        await bus.Received(1)
            .Publish(
                Arg.Is<StockWebsiteDiscovered>(e =>
                    e.CommonStockId == paris.Id
                    && e.Ticker == "BNP"
                    && e.MarketIdentifierCode == "XPAR"
                ),
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .Publish(
                Arg.Is<StockWebsiteDiscovered>(e =>
                    e.CommonStockId == nyse.Id && e.MarketIdentifierCode == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LegacyVenuePresentation_IsNeverACandidate()
    {
        var options = NewDbOptions();
        EquityIssuer legacy = await SeedVenueStock(options, "OLD", EquityIdentityState.Legacy);
        var source = new StubSource(10, new Dictionary<string, string> { ["OLD"] = "www.old.com" });

        await BuildSut(options, [source]).Import(CancellationToken.None);

        source.SeenTickers.Should().BeEmpty("an unverified non-US presentation is not directory");
        EquityIssuer reloaded = await Reload(options, legacy.Id);
        reloaded.Website.Should().BeNull();
        reloaded.WebsiteCheckedAt.Should().BeNull();
    }

    private sealed class StubSource : IWebsiteSource
    {
        private readonly Dictionary<string, string> _answers;

        public StubSource(int priority, Dictionary<string, string> answers)
        {
            Priority = priority;
            _answers = answers;
        }

        public List<string> SeenTickers { get; } = [];

        public List<WebsiteSourceStock> SeenStocks { get; } = [];

        public int Priority { get; }

        public string Name => $"stub-{Priority}";

        public Task<IReadOnlyDictionary<Guid, string>> FindWebsites(
            IReadOnlyList<WebsiteSourceStock> stocks,
            CancellationToken cancellationToken
        )
        {
            SeenTickers.AddRange(stocks.Select(s => s.Ticker));
            SeenStocks.AddRange(stocks);
            IReadOnlyDictionary<Guid, string> result = stocks
                .Where(s => _answers.ContainsKey(s.Ticker))
                .ToDictionary(s => s.Id, s => _answers[s.Ticker]);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingSource : IWebsiteSource
    {
        public ThrowingSource(int priority) => Priority = priority;

        public int Priority { get; }

        public string Name => "throwing";

        public Task<IReadOnlyDictionary<Guid, string>> FindWebsites(
            IReadOnlyList<WebsiteSourceStock> stocks,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("source backend down");
    }

    private sealed class FixedStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public FixedStatusHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(_status));
    }
}

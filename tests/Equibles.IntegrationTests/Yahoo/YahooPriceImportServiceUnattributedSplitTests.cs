using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.EquityMarkets.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

// Splits captured in July 2026, before splits recorded their listing, bound every series of their
// issuer and refused every history replacement. Production held a 1:4 for Adaptive Biotechnologies
// on 2026-07-15 that never happened (the chart, recorded verbatim, lists no split and trades
// $21 to $23 across the date) and a 3:1 for Willis Lease on 2026-07-20 beside the listing's own
// 3:1 of 2026-07-21.
[Collection(ParadeDbCollection.Name)]
public class YahooPriceImportServiceUnattributedSplitTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly Floor = new(2026, 5, 1);
    private static readonly DateOnly Today = new(2026, 9, 30);
    private static readonly DateOnly PhantomDate = new(2026, 7, 15);

    [Fact]
    public async Task ReconcileStock_ProviderWithdrewTheSplit_RemovesTheRowAndReplacesTheHistory()
    {
        InitializeServices();
        var chart = await RecordedChart("ADPT", "adpt-2026-05-01-to-2026-09-30.json");
        chart.Splits.Should().BeEmpty();
        var issuer = CreateStock("ADPT", "Adaptive Biotechnologies Corp");
        await SeedStocks(issuer);
        await SeedBars(issuer.Presentation.Listing, chart.Prices.Take(20));
        _splitRepo.Add(Unattributed(issuer, PhantomDate, 1m, 4m));
        await _splitRepo.SaveChanges();
        _yahooClient.GetChart("ADPT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);

        await ReRead(issuer);

        _splitRepo.GetAll().Should().BeEmpty();
        StoredBars(issuer.Presentation.EquityListingId)
            .Should()
            .HaveCount(chart.Prices.Count(bar => bar.Date < Today));
    }

    [Fact]
    public async Task ReconcileStock_ListingStatesTheSameEvent_RemovesTheDuplicateRow()
    {
        InitializeServices();
        var chart = await RecordedChart("WLFC", "wlfc-2026-05-01-to-2026-09-30.json");
        var issuer = CreateStock("WLFC", "Willis Lease Finance Corp");
        await SeedStocks(issuer);
        await SeedBars(issuer.Presentation.Listing, chart.Prices.Take(20));
        _splitRepo.Add(Unattributed(issuer, new DateOnly(2026, 7, 20), 3m, 1m));
        await _splitRepo.SaveChanges();
        _yahooClient.GetChart("WLFC", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);

        await ReRead(issuer);

        var split = _splitRepo.GetAll().Should().ContainSingle().Subject;
        split.EquityListingId.Should().Be(issuer.Presentation.EquityListingId);
        (split.EffectiveDate, split.Numerator, split.Denominator)
            .Should()
            .Be((new DateOnly(2026, 7, 21), 3m, 1m));
        StoredBars(issuer.Presentation.EquityListingId)
            .Should()
            .HaveCount(chart.Prices.Count(bar => bar.Date < Today));
    }

    // The provider lists no split, but its bars still jump by the ratio at the date: a real event
    // it dropped from its report. Nothing proves the series comparable, so the row keeps bounding it.
    [Fact]
    public async Task ReconcileStock_BarsJumpByTheRatioAtTheDate_KeepsTheRowAndTheStoredSeries()
    {
        InitializeServices();
        var chart = await RecordedChart("ADPT", "adpt-2026-05-01-to-2026-09-30.json");
        foreach (var bar in chart.Prices.Where(bar => bar.Date < PhantomDate))
        {
            bar.Open /= 4m;
            bar.High /= 4m;
            bar.Low /= 4m;
            bar.Close /= 4m;
            bar.AdjustedClose /= 4m;
        }
        var issuer = CreateStock("ADPT", "Adaptive Biotechnologies Corp");
        await SeedStocks(issuer);
        await SeedBars(issuer.Presentation.Listing, chart.Prices.Take(20));
        _splitRepo.Add(Unattributed(issuer, PhantomDate, 1m, 4m));
        await _splitRepo.SaveChanges();
        _yahooClient.GetChart("ADPT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);

        await ReRead(issuer);

        _splitRepo.GetAll().Should().ContainSingle().Which.EquityListingId.Should().BeNull();
        StoredBars(issuer.Presentation.EquityListingId).Should().HaveCount(20);
    }

    // The row bounds every series of the issuer, and this listing's evidence says nothing about
    // a sibling line whose stored bars cross the date without stating the event.
    [Fact]
    public async Task ReconcileStock_SiblingSeriesCrossesTheDate_KeepsTheRow()
    {
        InitializeServices();
        var chart = await RecordedChart("ADPT", "adpt-2026-05-01-to-2026-09-30.json");
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ADPT",
            Name: "Adaptive Biotechnologies Corp",
            Cik: "CIK-ADPT",
            SecondaryTickers: ["ADPTW"]
        );
        await SeedStocks(issuer);
        await SeedBars(issuer.Presentation.Listing, chart.Prices.Take(20));
        var sibling = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "ADPTW");
        await SeedBars(sibling, [chart.Prices[0], chart.Prices[^1]]);
        _splitRepo.Add(Unattributed(issuer, PhantomDate, 1m, 4m));
        await _splitRepo.SaveChanges();
        _yahooClient.GetChart("ADPT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);

        await ReRead(issuer);

        _splitRepo.GetAll().Should().ContainSingle();
        StoredBars(issuer.Presentation.EquityListingId).Should().HaveCount(20);
    }

    [Fact]
    public async Task ResettleUnattributedSplits_ReReadsEachPresentedIssuerOnce()
    {
        InitializeServices();
        var chart = await RecordedChart("ADPT", "adpt-2026-05-01-to-2026-09-30.json");
        var issuer = CreateStock("ADPT", "Adaptive Biotechnologies Corp");
        await SeedStocks(issuer);
        await SeedBars(issuer.Presentation.Listing, chart.Prices.Take(20));
        _splitRepo.Add(Unattributed(issuer, PhantomDate, 1m, 4m));
        await _splitRepo.SaveChanges();
        _yahooClient.GetChart("ADPT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);

        var attempted = await _service.ResettleUnattributedSplits([], 10, CancellationToken.None);

        attempted.Should().Equal(issuer.Id);
        _splitRepo.GetAll().Should().BeEmpty();
        (await _service.ResettleUnattributedSplits(attempted, 10, CancellationToken.None))
            .Should()
            .BeEmpty();
    }

    private Task ReRead(EquityIssuer issuer) =>
        _service.ReconcileStock(
            new PendingPriceReconciliationSeries(
                issuer.Id,
                issuer.Presentation.Listing.Ticker,
                [],
                [],
                issuer.Presentation.EquityListingId
            ),
            Floor,
            Today,
            CancellationToken.None,
            new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc)
        );

    private List<EquityDailyStockPrice> StoredBars(Guid listingId) =>
        _priceRepo.GetAllSeries().Where(price => price.EquityListingId == listingId).ToList();

    // The production shape: captured the day of the event, with neither listing nor series symbol.
    private static StockSplit Unattributed(
        EquityIssuer issuer,
        DateOnly date,
        decimal numerator,
        decimal denominator
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            EffectiveDate = date,
            Numerator = numerator,
            Denominator = denominator,
            Source = StockSplitSource.Yahoo,
            CreationTime = date.ToDateTime(new TimeOnly(11, 36), DateTimeKind.Utc),
            PriceAdjustmentAppliedTime = date.ToDateTime(new TimeOnly(12, 8), DateTimeKind.Utc),
        };

    private EquiblesFinancialDbContext _dbContext;
    private EquityDailyStockPriceRepository _priceRepo;
    private EquityIssuerRepository _stockRepo;
    private StockSplitRepository _splitRepo;
    private IYahooFinanceClient _yahooClient;
    private YahooPriceImportService _service;

    private void InitializeServices()
    {
        _dbContext = DbContext;
        _priceRepo = new EquityDailyStockPriceRepository(_dbContext);
        _stockRepo = new EquityIssuerRepository(_dbContext);
        _splitRepo = new StockSplitRepository(_dbContext);
        var dividendRepo = new CashDividendRepository(_dbContext);
        _yahooClient = Substitute.For<IYahooFinanceClient>();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (
                typeof(EquityMarketRegistrationRepository),
                new EquityMarketRegistrationRepository(_dbContext)
            ),
            (typeof(EquityDailyStockPriceRepository), _priceRepo),
            (typeof(EquityIssuerRepository), _stockRepo),
            (typeof(EquityListingRepository), new EquityListingRepository(_dbContext)),
            (typeof(StockSplitRepository), _splitRepo),
            (typeof(ISharesOutstandingProvider), Substitute.For<ISharesOutstandingProvider>()),
            (
                typeof(CorporateActionPriceReconciliationManager),
                new CorporateActionPriceReconciliationManager(
                    _splitRepo,
                    dividendRepo,
                    _stockRepo,
                    new CorporateActionPriceReconciliationCursorRepository(_dbContext)
                )
            ),
            (
                typeof(StockSplitCaptureManager),
                new StockSplitCaptureManager(_splitRepo, _stockRepo)
            ),
            (
                typeof(CashDividendCaptureManager),
                new CashDividendCaptureManager(dividendRepo, _stockRepo)
            )
        );

        _service = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _yahooClient,
            new TickerMapService(scopeFactory),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions { MinSyncDate = Floor.ToDateTime(TimeOnly.MinValue) }),
            Options.Create(new YahooPriceScraperOptions())
        );
    }

    private static EquityIssuer CreateStock(string ticker, string name) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: name,
            Cik: $"CIK-{ticker}"
        );

    private async Task SeedStocks(params EquityIssuer[] stocks)
    {
        _stockRepo.AddRange(stocks);
        await _stockRepo.SaveChanges();
        foreach (var stock in stocks)
            Equibles.TestSupport.NativeListingSeed.ForStock(_dbContext, stock);
        await _stockRepo.SaveChanges();
    }

    private async Task SeedBars(EquityListing listing, IEnumerable<HistoricalPrice> bars)
    {
        _priceRepo.AddRange(
            bars.Select(bar => new EquityDailyStockPrice
            {
                Id = Guid.NewGuid(),
                Listing = listing,
                EquityListingId = listing.Id,
                SourceTicker = listing.Ticker,
                Date = bar.Date,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                AdjustedClose = bar.AdjustedClose,
                Volume = bar.Volume,
                CreationTime = new DateTime(2026, 7, 1, 4, 0, 0, DateTimeKind.Utc),
            })
        );
        await _priceRepo.SaveChanges();
    }

    // Parses the verbatim recorded response through the production client.
    private static async Task<YahooChartData> RecordedChart(string ticker, string file)
    {
        var body = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Yahoo",
                "UnattributedSplits",
                file
            )
        );
        using var http = new HttpClient(new RecordedResponseHandler(body));
        return await new RecordedYahooClient(http).GetChart(ticker, Floor, Today);
    }

    private sealed class RecordedYahooClient(HttpClient client)
        : YahooFinanceClient(
            client,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<YahooFinanceClient>.Instance
        )
    {
        protected override Task<(string Crumb, string CookieHeader)> EnsureSession() =>
            Task.FromResult(("test-crumb", "A=1"));
    }

    private sealed class RecordedResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
    }
}

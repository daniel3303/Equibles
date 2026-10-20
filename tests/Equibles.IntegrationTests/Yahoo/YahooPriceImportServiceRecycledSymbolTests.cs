using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.EquityMarkets.Data;
using Equibles.EquityMarkets.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

// Yahoo serves a bare U.S. symbol as its current holder's series. The recorded charts are what
// Yahoo returned for two retired when-issued symbols: MMMW is Mass Megawatts' OTC line (which
// 3M's retired MMMW listing stored as its own), IPW is iPower's Nasdaq line (which International
// Paper's retired IPW listing stored).
public class YahooPriceImportServiceRecycledSymbolTests : IDisposable
{
    private static readonly DateOnly MmmwFirst = new(2024, 3, 25);
    private static readonly DateOnly MmmwDelisted = new(2024, 4, 1);

    [Fact]
    public async Task Import_RetiredSymbolHeldByAnotherIssuer_NeverTakesTheHoldersSeries()
    {
        _workerOptions.MinSyncDate = MmmwFirst.ToDateTime(TimeOnly.MinValue);
        var chart = await RecordedChart(
            "mmmw-2024-03-25-to-2024-04-01.json",
            "MMMW",
            MmmwFirst,
            MmmwDelisted
        );
        var holder = CreateStock("MMMW", "Holder of the symbol");
        var retiredIssuer = CreateStock("MMM", "Retired when-issued filer");
        await SeedStocks(holder, retiredIssuer);
        var retired = await SeedRetiredListing(retiredIssuer, "MMMW", MmmwDelisted);
        await SeedBars(holder.Presentation.Listing, chart.Prices);
        _yahooClient.GetChart("MMMW", MmmwFirst, MmmwDelisted).Returns(chart);

        await _service.Import(includeEnrichment: false, CancellationToken.None);

        await _yahooClient.DidNotReceive().GetChart("MMMW", Arg.Any<DateOnly>(), MmmwDelisted);
        BarDates(retired).Should().BeEmpty();
        BarDates(holder.Presentation.Listing).Should().Equal(chart.Prices.Select(bar => bar.Date));
        _splitRepo.GetAll().Should().BeEmpty();
        Evidence(retiredIssuer).HistoricalPriceBackfillAttemptedAt.Should().BeNull();
    }

    [Fact]
    public async Task Import_RetiredListingStoringTheHoldersSeries_PurgesThoseBarsThroughTheLane()
    {
        _workerOptions.MinSyncDate = MmmwFirst.ToDateTime(TimeOnly.MinValue);
        var chart = await RecordedChart(
            "mmmw-2024-03-25-to-2024-04-01.json",
            "MMMW",
            MmmwFirst,
            MmmwDelisted
        );
        var holder = CreateStock("MMMW", "Holder of the symbol");
        var retiredIssuer = CreateStock("MMM", "Retired when-issued filer");
        await SeedStocks(holder, retiredIssuer);
        var retired = await SeedRetiredListing(
            retiredIssuer,
            "MMMW",
            MmmwDelisted,
            backfilledAt: DateTime.UtcNow
        );
        await SeedBars(holder.Presentation.Listing, chart.Prices);
        // What production stored: the holder's bars, copied onto the retired listing.
        await SeedBars(retired, chart.Prices);

        await _service.Import(includeEnrichment: false, CancellationToken.None);

        BarDates(retired).Should().BeEmpty();
        BarDates(holder.Presentation.Listing).Should().Equal(chart.Prices.Select(bar => bar.Date));
        ReloadListing(retired).PriceHistoryBackfilled.Should().BeFalse();
        Evidence(retiredIssuer).HistoricalPriceBackfillAttemptedAt.Should().NotBeNull();
        await _yahooClient.DidNotReceive().GetChart("MMMW", Arg.Any<DateOnly>(), MmmwDelisted);

        // Healed: nothing is left inside the holder's window, so the next pass leaves it alone.
        var stamped = Evidence(retiredIssuer).HistoricalPriceBackfillAttemptedAt;
        await _service.Import(includeEnrichment: false, CancellationToken.None);
        Evidence(retiredIssuer).HistoricalPriceBackfillAttemptedAt.Should().Be(stamped);
        BarDates(retired).Should().BeEmpty();
    }

    [Fact]
    public async Task Import_SymbolReusedAfterDelisting_KeepsTheRetiredListingsOwnBars()
    {
        var floor = new DateOnly(2023, 1, 2);
        var delistedOn = new DateOnly(2023, 1, 10);
        _workerOptions.MinSyncDate = floor.ToDateTime(TimeOnly.MinValue);
        var successor = CreateStock("REUSE", "Later holder of the symbol");
        var retiredIssuer = CreateStock("PARENT", "Earlier holder of the symbol");
        await SeedStocks(successor, retiredIssuer);
        var retired = await SeedRetiredListing(
            retiredIssuer,
            "REUSE",
            delistedOn,
            backfilledAt: DateTime.UtcNow
        );
        await SeedBars(retired, Bars(10m, new DateOnly(2023, 1, 9), delistedOn));
        await SeedBars(
            successor.Presentation.Listing,
            Bars(50m, new DateOnly(2023, 2, 1), new DateOnly(2023, 2, 2))
        );

        await _service.Import(includeEnrichment: false, CancellationToken.None);

        BarDates(retired).Should().Equal(new DateOnly(2023, 1, 9), delistedOn);
        ReloadListing(retired).PriceHistoryBackfilled.Should().BeTrue();
        await _yahooClient.DidNotReceive().GetChart("REUSE", Arg.Any<DateOnly>(), delistedOn);
    }

    [Fact]
    public async Task Import_SymbolClaimedDuringTheFetch_RefusesTheWrite()
    {
        var first = new DateOnly(2021, 9, 27);
        var delistedOn = new DateOnly(2021, 10, 1);
        _workerOptions.MinSyncDate = first.ToDateTime(TimeOnly.MinValue);
        var chart = await RecordedChart(
            "ipw-2021-09-27-to-2021-10-01.json",
            "IPW",
            first,
            delistedOn
        );
        var retiredIssuer = CreateStock("IP", "Retired when-issued filer");
        await SeedStocks(retiredIssuer);
        var retired = await SeedRetiredListing(retiredIssuer, "IPW", delistedOn);
        _yahooClient
            .GetChart("IPW", first, delistedOn)
            .Returns(_ =>
            {
                // The directory assigns the symbol to its current holder between selection and write.
                _stockRepo.Add(CreateStock("IPW", "Current holder of the symbol"));
                _stockRepo.SaveChanges().GetAwaiter().GetResult();
                return chart;
            });

        await _service.Import(includeEnrichment: false, CancellationToken.None);

        await _yahooClient.Received(1).GetChart("IPW", first, delistedOn);
        BarDates(retired).Should().BeEmpty();
        ReloadListing(retired).PriceHistoryBackfilled.Should().BeFalse();
        _splitRepo.GetAll().Should().BeEmpty();
    }

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly EquityDailyStockPriceRepository _priceRepo;
    private readonly EquityIssuerRepository _stockRepo;
    private readonly StockSplitRepository _splitRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly WorkerOptions _workerOptions;
    private readonly YahooPriceImportService _service;

    public YahooPriceImportServiceRecycledSymbolTests()
    {
        _dbContext =
            TestDbContextFactory.CreateIgnoringInMemoryTransactionsWithoutPriceSeedDefaults(
                new EquityMarketsModuleConfiguration(),
                new CommonStocksModuleConfiguration(),
                new YahooModuleConfiguration()
            );
        _priceRepo = new EquityDailyStockPriceRepository(_dbContext);
        _stockRepo = new EquityIssuerRepository(_dbContext);
        _splitRepo = new StockSplitRepository(_dbContext);
        var dividendRepo = new CashDividendRepository(_dbContext);
        _yahooClient = Substitute.For<IYahooFinanceClient>();
        _yahooClient
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _workerOptions = new WorkerOptions();

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
            Options.Create(_workerOptions),
            Options.Create(new YahooPriceScraperOptions())
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
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

    private async Task<EquityListing> SeedRetiredListing(
        EquityIssuer issuer,
        string ticker,
        DateOnly delistedOn,
        DateTime? backfilledAt = null
    )
    {
        var listing = Equibles.TestSupport.NativeListingSeed.ForStock(_dbContext, issuer, ticker);
        listing.Active = false;
        listing.DelistedOn = delistedOn;
        listing.PriceHistoryBackfilled = backfilledAt != null;
        _stockRepo.AddDelistedListing(
            new EquityListingRetirementEvidence
            {
                EquityIssuerId = issuer.Id,
                ListedTicker = ticker,
                DelistedOn = delistedOn,
                HistoricalPriceBackfillAttemptedAt = backfilledAt,
            }
        );
        await _stockRepo.SaveChanges();
        return listing;
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
            })
        );
        await _priceRepo.SaveChanges();
    }

    private static IEnumerable<HistoricalPrice> Bars(decimal close, params DateOnly[] dates) =>
        dates.Select(date => new HistoricalPrice
        {
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = 1_000,
        });

    private List<DateOnly> BarDates(EquityListing listing) =>
        _priceRepo
            .GetAllSeries()
            .Where(price => price.EquityListingId == listing.Id)
            .OrderBy(price => price.Date)
            .Select(price => price.Date)
            .ToList();

    private EquityListing ReloadListing(EquityListing listing) =>
        _dbContext.Set<EquityListing>().Single(row => row.Id == listing.Id);

    private EquityListingRetirementEvidence Evidence(EquityIssuer issuer) =>
        _stockRepo.GetDelistedListings().Single(row => row.EquityIssuerId == issuer.Id);

    // Parses the verbatim recorded response through the production client.
    private static async Task<YahooChartData> RecordedChart(
        string file,
        string ticker,
        DateOnly start,
        DateOnly end
    )
    {
        var body = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Yahoo", "RecycledSymbols", file)
        );
        using var http = new HttpClient(new RecordedResponseHandler(body));
        var chart = await new RecordedYahooClient(http).GetChart(ticker, start, end);
        chart.SourceIdentity.Symbol.Should().Be(ticker);
        chart.Prices.Should().NotBeEmpty();
        return chart;
    }

    private sealed class RecordedYahooClient(HttpClient client)
        : YahooFinanceClient(client, NullLogger<YahooFinanceClient>.Instance)
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

using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
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
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

// MGN executed one 1:30 reverse split on 2026-09-17. The reference feed first reported it as 1:40
// for 21 August, then 1:40 for 8 September, and every row was reconciled; Yahoo's chart (recorded
// verbatim) still lists the withdrawn 1:40 and serves every bar before 8 September forty times too
// high. Production stored exactly that serve, with all three feed rows stamped applied.
[Collection(ParadeDbCollection.Name)]
public class YahooPriceImportServiceRevisedSplitTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly Floor = new(2025, 8, 1);
    private static readonly DateOnly Today = new(2026, 10, 22);
    private static readonly DateOnly Executed = new(2026, 9, 17);
    private static readonly DateOnly Withdrawn = new(2026, 9, 8);

    [Fact]
    public async Task ReconcileStock_RecordedChartKeepsTheWithdrawnRatio_StoresOneThirtyForOneBasis()
    {
        InitializeServices();
        var chart = await RecordedChart();
        var served = chart.Prices.ToDictionary(price => price.Date, price => price.Close);
        var issuer = CreateStock("MGN", "Megan Holdings");
        await SeedStocks(issuer);
        await SeedBars(issuer.Presentation.Listing, chart.Prices);
        SeedFeedRows(issuer);
        await _splitRepo.SaveChanges();
        // The feed's complete report now lists only the executed definition.
        await new StockSplitCaptureManager(_splitRepo, _stockRepo).SupersedeMovedDates(
            issuer.Id,
            issuer.Presentation.EquityListingId,
            "MGN",
            new DateOnly(2025, 9, 26),
            [
                new CapturedSplit
                {
                    EffectiveDate = Executed,
                    Numerator = 1m,
                    Denominator = 30m,
                    Source = StockSplitSource.External,
                },
            ]
        );
        _yahooClient
            .GetChart("MGN", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(_ => RecordedChart().GetAwaiter().GetResult());

        await ReconcilePending();

        var split = _splitRepo.GetAll().Should().ContainSingle().Subject;
        (split.EffectiveDate, split.Numerator, split.Denominator, split.Source)
            .Should()
            .Be((Executed, 1m, 30m, StockSplitSource.External));
        split.PriceAdjustmentAppliedTime.Should().NotBeNull();
        var stored = _priceRepo
            .GetAllSeries()
            .Where(price => price.EquityListingId == issuer.Presentation.EquityListingId)
            .ToDictionary(price => price.Date);
        stored.Should().HaveCount(served.Count);
        // Before the withdrawn date the serve carried the extra 1:40: 120.00 on 4 September traded
        // at about 0.10 before the 1:30, so its 1:30 basis is 3.00.
        stored[new DateOnly(2026, 9, 4)].Close.Should().BeApproximately(3.00m, 0.0001m);
        stored[new DateOnly(2026, 9, 4)].Volume.Should().Be(2_247 * 40);
        stored[new DateOnly(2025, 9, 26)].Close.Should().BeApproximately(145.50m, 0.0001m);
        foreach (var (date, close) in served.Where(pair => pair.Key < Withdrawn))
            stored[date].Close.Should().BeApproximately(close / 40m, 0.0001m);
        foreach (var (date, close) in served.Where(pair => pair.Key >= Withdrawn))
            stored[date].Close.Should().BeApproximately(close, 0.0001m);
    }

    // With no higher-precedence definition, Yahoo's two events are its own claim and nothing
    // outranks them, so its serve is stored as served.
    [Fact]
    public async Task ReconcileStock_OnlyYahooDefinesTheEvents_KeepsTheServeAsServed()
    {
        InitializeServices();
        var chart = await RecordedChart();
        var served = chart.Prices.ToDictionary(price => price.Date, price => price.Close);
        var issuer = CreateStock("MGN", "Megan Holdings");
        await SeedStocks(issuer);
        await SeedBars(issuer.Presentation.Listing, chart.Prices);
        foreach (var (date, denominator) in new[] { (Withdrawn, 40m), (Executed, 30m) })
        {
            var row = Row(
                issuer,
                date,
                denominator,
                new DateTime(2026, 9, 18, 4, 0, 0, DateTimeKind.Utc),
                null
            );
            row.Source = StockSplitSource.Yahoo;
            _splitRepo.Add(row);
        }
        await _splitRepo.SaveChanges();
        _yahooClient.GetChart("MGN", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);

        await ReconcilePending();

        _priceRepo
            .GetAllSeries()
            .Single(price => price.Date == new DateOnly(2026, 9, 4))
            .Close.Should()
            .BeApproximately(served[new DateOnly(2026, 9, 4)], 0.0001m);
        _splitRepo.GetAll().Should().HaveCount(2);
    }

    private async Task ReconcilePending()
    {
        var manager = new CorporateActionPriceReconciliationManager(
            _splitRepo,
            new CashDividendRepository(_dbContext),
            _stockRepo,
            new CorporateActionPriceReconciliationCursorRepository(_dbContext)
        );
        var selection = await manager.SelectPendingSeries(0, Today);
        var series = selection.Series.Should().ContainSingle().Subject;
        await _service.ReconcileStock(
            series,
            Floor,
            Today,
            CancellationToken.None,
            new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc)
        );
    }

    // The production rows on 2026-10-22, each stamped applied.
    private void SeedFeedRows(EquityIssuer issuer)
    {
        _splitRepo.Add(
            Row(
                issuer,
                new DateOnly(2026, 8, 21),
                40m,
                new DateTime(2026, 8, 12, 16, 54, 29, DateTimeKind.Utc),
                new DateTime(2026, 8, 22, 0, 52, 47, DateTimeKind.Utc)
            )
        );
        _splitRepo.Add(
            Row(
                issuer,
                Withdrawn,
                40m,
                new DateTime(2026, 8, 27, 20, 22, 52, DateTimeKind.Utc),
                new DateTime(2026, 9, 9, 13, 53, 38, DateTimeKind.Utc)
            )
        );
        _splitRepo.Add(
            Row(
                issuer,
                Executed,
                30m,
                new DateTime(2026, 9, 10, 16, 24, 39, DateTimeKind.Utc),
                new DateTime(2026, 9, 18, 0, 51, 9, DateTimeKind.Utc)
            )
        );
    }

    private static StockSplit Row(
        EquityIssuer issuer,
        DateOnly date,
        decimal denominator,
        DateTime capturedAt,
        DateTime? applied
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            EquityListingId = issuer.Presentation.EquityListingId,
            PriceSeriesTicker = "MGN",
            EffectiveDate = date,
            Numerator = 1m,
            Denominator = denominator,
            Source = StockSplitSource.External,
            CreationTime = capturedAt,
            PriceAdjustmentAppliedTime = applied,
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
            Options.Create(new WorkerOptions()),
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
                CreationTime = new DateTime(2026, 9, 22, 4, 0, 0, DateTimeKind.Utc),
            })
        );
        await _priceRepo.SaveChanges();
    }

    // Parses the verbatim recorded response through the production client.
    private static async Task<YahooChartData> RecordedChart()
    {
        var body = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Yahoo",
                "RevisedSplits",
                "mgn-2025-08-01-to-2026-10-22.json"
            )
        );
        using var http = new HttpClient(new RecordedResponseHandler(body));
        var chart = await new RecordedYahooClient(http).GetChart("MGN", Floor, Today);
        chart.Splits.Should().HaveCount(2);
        return chart;
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

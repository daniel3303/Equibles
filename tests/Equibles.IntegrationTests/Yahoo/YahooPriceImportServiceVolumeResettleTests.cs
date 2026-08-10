using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Calendars;
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

/// <summary>
/// End-to-end cover for stored-bar resettlement. The pure rules are pinned in the unit suite; what
/// these need to prove is the WIRING, which is where the bug actually lived: the fetch window has
/// to reach back far enough to re-request an already-stored bar, and the correction has to run
/// before the insert path's "nothing new to insert" early return.
///
/// Every case is set up the way steady-state operation actually reaches the correction: the stock
/// is one settled session behind, so the new bar is what drives the fetch and the older stored bar
/// is corrected by riding it. A stock that is FULLY current is deliberately not covered, because
/// it deliberately does not fetch at all — re-reading it would cost a call per stock per cycle to
/// buy less than a day of freshness (see the settled-trading-day gate in ResolveStartDate).
///
/// Real numbers from the defect: KRC's 2026-07-27 bar was stored with 1,183,039 shares hours after
/// the close, while the settled figure was 1,573,891.
/// </summary>
public class YahooPriceImportServiceVolumeResettleTests : IDisposable
{
    private const long UnsettledVolume = 1_183_039;
    private const long SettledVolume = 1_573_891;

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly DailyStockPriceRepository _priceRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly YahooPriceImportService _service;

    // Wide enough that the two most recent trading days are inside it whatever weekday the suite
    // runs on, so the cases exercise the wiring rather than the calendar. The default window's own
    // boundary is pinned in the unit suite.
    private const int WindowDays = 30;

    public YahooPriceImportServiceVolumeResettleTests()
    {
        _dbContext = TestDbContextFactory.CreateIgnoringInMemoryTransactions(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _priceRepo = new DailyStockPriceRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);
        var splitRepo = new StockSplitRepository(_dbContext);
        var dividendRepo = new CashDividendRepository(_dbContext);

        _yahooClient = Substitute.For<IYahooFinanceClient>();
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DailyStockPriceRepository), _priceRepo),
            (typeof(CommonStockRepository), _stockRepo),
            (typeof(ISharesOutstandingProvider), Substitute.For<ISharesOutstandingProvider>()),
            (
                typeof(CorporateActionPriceReconciliationManager),
                new CorporateActionPriceReconciliationManager(
                    splitRepo,
                    dividendRepo,
                    _stockRepo,
                    new CorporateActionPriceReconciliationCursorRepository(_dbContext)
                )
            ),
            (typeof(StockSplitCaptureManager), new StockSplitCaptureManager(splitRepo, _stockRepo))
        );

        _service = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _yahooClient,
            new TickerMapService(scopeFactory),
            errorReporter,
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions { VolumeResettleWindowDays = WindowDays })
        );

        _yahooClient.GetKeyStatistics(Arg.Any<string>()).Returns((KeyStatistics)null);
        _yahooClient.GetCompanyProfile(Arg.Any<string>()).Returns((CompanyProfile)null);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Import_StoredBarVolumeSettledUpward_IsCorrected()
    {
        // The exact shape of an ordinary daily cycle: the stock holds the previous session and the
        // newest settled one is missing, so the fetch is driven by that new bar. The stored bar's
        // correction has to ride it — nothing else requests that date.
        var stock = SeedStock("KRC");
        var (newest, previous) = TwoMostRecentSettledSessions();
        SeedPrice(stock, previous, UnsettledVolume);

        var requestedStart = default(DateOnly);
        _yahooClient
            .GetChart("KRC", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(call =>
            {
                requestedStart = (DateOnly)call[1];
                return Chart((previous, SettledVolume), (newest, SettledVolume));
            });

        await _service.Import(CancellationToken.None);

        // The window must actually reach the stored bar — the forward-only start date alone would
        // begin after it, and the feed would never be asked to re-serve it.
        requestedStart.Should().BeOnOrBefore(previous);

        _priceRepo.GetAll().Single(p => p.Date == previous).Volume.Should().Be(SettledVolume);
        // The new session still lands: the correction must not displace the insert path.
        _priceRepo.GetAll().Single(p => p.Date == newest).Volume.Should().Be(SettledVolume);
    }

    [Fact]
    public async Task Import_FeedServesLowerVolume_KeepsTheStoredFigure()
    {
        // A degraded re-serve (a partial response, a venue dropping out) must never walk a settled
        // figure back down. Without the upgrade-only rule the repair would oscillate with the feed.
        var stock = SeedStock("KRC");
        var (newest, previous) = TwoMostRecentSettledSessions();
        SeedPrice(stock, previous, SettledVolume);

        _yahooClient
            .GetChart("KRC", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(Chart((previous, UnsettledVolume), (newest, SettledVolume)));

        await _service.Import(CancellationToken.None);

        _priceRepo.GetAll().Single(p => p.Date == previous).Volume.Should().Be(SettledVolume);
    }

    [Fact]
    public async Task Import_FeedRevisesRecentOhlc_CorrectsPricesButPreservesAdjustedClose()
    {
        var stock = SeedStock("CBOE");
        var (newest, previous) = TwoMostRecentSettledSessions();
        var stored = SeedPrice(stock, previous, UnsettledVolume, 310.23m);
        stored.Open = 287.54m;
        stored.High = 310.88m;
        stored.Low = 287.76m;
        stored.AdjustedClose = 309.11m;
        await _priceRepo.SaveChanges();

        _yahooClient
            .GetChart("CBOE", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(
                Chart(
                    Bar(previous, 287.54m, 311.06m, 287.54m, 310.23m, SettledVolume),
                    Bar(newest, 310.50m, 312.00m, 309.90m, 311.40m, SettledVolume)
                )
            );

        await _service.Import(CancellationToken.None);

        var corrected = _priceRepo.GetAll().Single(p => p.Date == previous);
        corrected.Open.Should().Be(287.54m);
        corrected.High.Should().Be(311.06m);
        corrected.Low.Should().Be(287.54m);
        corrected.Close.Should().Be(310.23m);
        corrected.AdjustedClose.Should().Be(309.11m);
        corrected.Volume.Should().Be(SettledVolume);
    }

    [Fact]
    public async Task RepairInvalidOhlc_AuthoritativeBarExists_ReplacesHistoricalPrices()
    {
        var stock = SeedStock("CBOE");
        var date = new DateOnly(2026, 7, 31);
        var stored = SeedPrice(stock, date, UnsettledVolume, 310.23m);
        stored.Open = 287.54m;
        stored.High = 310.88m;
        stored.Low = 287.76m;
        stored.AdjustedClose = 309.11m;
        await _priceRepo.SaveChanges();

        _yahooClient
            .GetChart("CBOE", date, date)
            .Returns(Chart(Bar(date, 287.54m, 311.06m, 287.54m, 310.23m, SettledVolume)));

        var complete = await _service.RepairInvalidOhlc(CancellationToken.None);

        complete.Should().BeTrue();
        var repaired = _priceRepo.GetAll().Single();
        repaired.Open.Should().Be(287.54m);
        repaired.High.Should().Be(311.06m);
        repaired.Low.Should().Be(287.54m);
        repaired.Close.Should().Be(310.23m);
        repaired.AdjustedClose.Should().Be(309.11m);
        repaired.Volume.Should().Be(SettledVolume);
    }

    [Fact]
    public async Task RepairInvalidOhlc_NoValidAuthoritativeBar_RemovesKnownBadRow()
    {
        var stock = SeedStock("CBOE");
        var date = new DateOnly(2026, 7, 31);
        var stored = SeedPrice(stock, date, UnsettledVolume, 310.23m);
        stored.Low = 311m;
        await _priceRepo.SaveChanges();

        _yahooClient.GetChart("CBOE", date, date).Returns(new YahooChartData());

        var complete = await _service.RepairInvalidOhlc(CancellationToken.None);

        complete.Should().BeTrue();
        _priceRepo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Import_BarOlderThanTheWindow_IsLeftAlone()
    {
        // The window bounds the repair. A bar from before it keeps its stored volume even when the
        // response carries a different figure for that date, so a cycle can never rewrite
        // arbitrarily deep history off one chart call — only the widened window may.
        var stock = SeedStock("KRC");
        var (newest, previous) = TwoMostRecentSettledSessions();
        var beyondWindow = previous.AddDays(-(WindowDays + 30));
        SeedPrice(stock, beyondWindow, UnsettledVolume);
        SeedPrice(stock, previous, UnsettledVolume);

        _yahooClient
            .GetChart("KRC", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(
                Chart(
                    (beyondWindow, SettledVolume),
                    (previous, SettledVolume),
                    (newest, SettledVolume)
                )
            );

        await _service.Import(CancellationToken.None);

        _priceRepo.GetAll().Single(p => p.Date == beyondWindow).Volume.Should().Be(UnsettledVolume);
        _priceRepo.GetAll().Single(p => p.Date == previous).Volume.Should().Be(SettledVolume);
    }

    [Fact]
    public async Task Import_StoredRowOnAPostReverseSplitBasis_IsNotOverwrittenByAsTradedVolume()
    {
        // The corruption case. A reconciled 1:25 reverse-split row holds an adjusted close 25x
        // larger and an adjusted volume 25x smaller than the as-traded session the feed is still
        // serving for that date. The as-traded volume therefore always looks like a settlement
        // upgrade, and writing it would inflate the stock's volume history by the split ratio.
        var stock = SeedStock("PRPL");
        var (newest, previous) = TwoMostRecentSettledSessions();
        const decimal adjustedClose = 8.05m;
        const long adjustedVolume = 23_560;
        SeedPrice(stock, previous, adjustedVolume, adjustedClose);

        // The feed serves that same session as-traded: close 25x smaller, volume 25x larger.
        _yahooClient
            .GetChart("PRPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(Chart(close: 0.322m, bars: [(previous, 589_000), (newest, 22_247)]));

        await _service.Import(CancellationToken.None);

        var stored = _priceRepo.GetAll().Single(p => p.Date == previous);
        stored.Volume.Should().Be(adjustedVolume);
        stored.Close.Should().Be(adjustedClose);
    }

    [Fact]
    public async Task Import_StoredAsTradedRowServedAdjusted_IsNotOverwrittenByAdjustedVolume()
    {
        // The PRE-reconcile ordering every split passes through: the split is captured at the end
        // of the cycle whose reconcile pass already ran, so for one full cycle the stored
        // pre-split rows are still as-traded while the feed already serves them adjusted. On a
        // forward split the adjusted volume is ratio-times larger, so without the basis guard it
        // would read as a settlement upgrade and leave a row with an adjusted volume under an
        // as-traded close. Real WLFC 3:1 numbers.
        var stock = SeedStock("WLFC");
        var (newest, previous) = TwoMostRecentSettledSessions();
        const decimal asTradedClose = 216.98m;
        const long asTradedVolume = 56_300;
        SeedPrice(stock, previous, asTradedVolume, asTradedClose);

        _yahooClient
            .GetChart("WLFC", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(Chart(close: 72.3267m, bars: [(previous, 168_879), (newest, 60_000)]));

        await _service.Import(CancellationToken.None);

        var storedRow = _priceRepo.GetAll().Single(p => p.Date == previous);
        storedRow.Volume.Should().Be(asTradedVolume);
        storedRow.Close.Should().Be(asTradedClose);
    }

    // The two most recent settled sessions, resolved off the real calendar so the cases behave the
    // same whatever weekday the suite runs on. The service derives "today" from the clock and never
    // stores the in-progress bar, so the newest settled session is the last trading day before it.
    private static (DateOnly Newest, DateOnly Previous) TwoMostRecentSettledSessions()
    {
        var newest = UsMarketCalendar.PreviousOrSameTradingDay(
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        );
        var previous = UsMarketCalendar.PreviousOrSameTradingDay(newest.AddDays(-1));
        return (newest, previous);
    }

    private static YahooChartData Chart(params (DateOnly Date, long Volume)[] bars) =>
        Chart(close: 39.41m, bars);

    private static YahooChartData Chart(params HistoricalPrice[] bars) =>
        new() { Prices = bars.ToList() };

    private static HistoricalPrice Bar(
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume
    ) =>
        new()
        {
            Date = date,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            AdjustedClose = close,
            Volume = volume,
        };

    // The close is what proves the split basis, so a case about basis has to be able to set it.
    private static YahooChartData Chart(decimal close, (DateOnly Date, long Volume)[] bars) =>
        new()
        {
            Prices = bars.Select(b => new HistoricalPrice
                {
                    Date = b.Date,
                    Open = close,
                    High = close,
                    Low = close,
                    Close = close,
                    AdjustedClose = close,
                    Volume = b.Volume,
                })
                .ToList(),
        };

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

    private DailyStockPrice SeedPrice(
        CommonStock stock,
        DateOnly date,
        long volume,
        decimal close = 39.41m
    )
    {
        var price = new DailyStockPrice
        {
            CommonStockId = stock.Id,
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = volume,
        };
        _priceRepo.Add(price);
        _priceRepo.SaveChanges().GetAwaiter().GetResult();
        return price;
    }
}

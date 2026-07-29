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
/// End-to-end cover for the volume resettle. The pure rules are pinned in the unit suite; what
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
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _priceRepo = new DailyStockPriceRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);
        var splitRepo = new StockSplitRepository(_dbContext);

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
                typeof(SplitPriceReconciliationManager),
                new SplitPriceReconciliationManager(splitRepo)
            ),
            (typeof(StockSplitCaptureManager), new StockSplitCaptureManager(splitRepo))
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
        new()
        {
            Prices = bars.Select(b => new HistoricalPrice
                {
                    Date = b.Date,
                    Open = 39.57m,
                    High = 39.70m,
                    Low = 39.03m,
                    Close = 39.41m,
                    AdjustedClose = 39.41m,
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

    private void SeedPrice(CommonStock stock, DateOnly date, long volume)
    {
        _priceRepo.Add(
            new DailyStockPrice
            {
                CommonStockId = stock.Id,
                Date = date,
                Open = 39.57m,
                High = 39.70m,
                Low = 39.03m,
                Close = 39.41m,
                AdjustedClose = 39.41m,
                Volume = volume,
            }
        );
        _priceRepo.SaveChanges().GetAwaiter().GetResult();
    }
}

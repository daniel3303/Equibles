using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Calendars;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

[Collection(ParadeDbCollection.Name)]
public class LisbonPriceImportTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly Session = new(2025, 7, 4);
    private readonly IYahooFinanceClient _client = Substitute.For<IYahooFinanceClient>();
    private readonly WorkerOptions _options = new() { MinSyncDate = new(2025, 7, 1) };

    private YahooPriceImportService Service()
    {
        var issuers = new EquityIssuerRepository(DbContext);
        var splits = new StockSplitRepository(DbContext);
        var dividends = new CashDividendRepository(DbContext);
        var scope = ServiceScopeSubstitute.Create(
            (typeof(EquityIssuerRepository), issuers),
            (typeof(EquityListingRepository), new EquityListingRepository(DbContext)),
            (
                typeof(EquityDailyStockPriceRepository),
                new EquityDailyStockPriceRepository(DbContext)
            ),
            (typeof(StockSplitRepository), splits),
            (typeof(CashDividendRepository), dividends),
            (typeof(StockSplitCaptureManager), new StockSplitCaptureManager(splits, issuers)),
            (
                typeof(CashDividendCaptureManager),
                new CashDividendCaptureManager(dividends, issuers)
            ),
            (
                typeof(CorporateActionPriceReconciliationManager),
                new CorporateActionPriceReconciliationManager(
                    splits,
                    dividends,
                    issuers,
                    new CorporateActionPriceReconciliationCursorRepository(DbContext)
                )
            )
        );
        return new(
            scope,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _client,
            new TickerMapService(scope),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(_options),
            Options.Create(new YahooPriceScraperOptions())
        );
    }

    private static PriceSeriesTarget Target(EquityListing listing) =>
        new(
            listing.Ticker,
            listing.Security.EquityIssuerId,
            listing.Id,
            false,
            MarketCountryCode: listing.MarketCountryCode,
            MarketIdentifierCode: listing.MarketIdentifierCode,
            Isin: listing.Security.Isin
        );

    private static Task Import(
        YahooPriceImportService service,
        PriceSeriesTarget target,
        DateOnly today
    ) =>
        (Task)
            typeof(YahooPriceImportService)
                .GetMethod("ImportTicker", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(service, [target, today, CancellationToken.None])!;

    private static Task<List<PriceSeriesTarget>> Targets(YahooPriceImportService service) =>
        (Task<List<PriceSeriesTarget>>)
            typeof(YahooPriceImportService)
                .GetMethod(
                    "BuildLisbonPriceTargets",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!
                .Invoke(service, [CancellationToken.None])!;

    private async Task<EquityListing> SeedLisbon(
        EquityIssuer issuer = null,
        string ticker = "SAME",
        string mic = "XLIS"
    )
    {
        issuer ??= new EquityIssuer { Name = "Lisbon issuer" };
        var security = new EquitySecurity
        {
            Issuer = issuer,
            Isin = "PTALT0AE0002",
            SecurityType = EquitySecurityKind.OrdinaryShare,
        };
        var listing = new EquityListing
        {
            Security = security,
            Ticker = ticker,
            MarketCountryCode = "PT",
            MarketIdentifierCode = mic,
            TradingCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
            IdentityState = EquityIdentityState.Verified,
            IdentitySourceUrl = "https://live.euronext.com/en/product/equities/PTALT0AE0002-XLIS",
        };
        DbContext.Add(listing);
        await DbContext.SaveChangesAsync();
        return listing;
    }

    private static EquityDailyStockPrice Price(
        EquityListing listing,
        DateOnly date,
        decimal close
    ) =>
        new()
        {
            Listing = listing,
            SourceTicker = listing.Ticker,
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = 100,
        };

    private static YahooChartData Chart(string symbol = "SAME.LS") =>
        new()
        {
            SourceIdentity = new()
            {
                Symbol = symbol,
                Currency = "EUR",
                ExchangeCode = "LIS",
                ExchangeName = "Lisbon",
                InstrumentType = "EQUITY",
                ExchangeTimeZone = "Europe/Lisbon",
            },
            Prices =
            [
                new HistoricalPrice
                {
                    Date = Session,
                    Open = 11,
                    High = 11,
                    Low = 11,
                    Close = 11,
                    AdjustedClose = 11,
                    Volume = 200,
                },
            ],
            Dividends = [new CashDividendEvent { Date = Session, Amount = .25m }],
        };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_UsHoliday_PreservesUsSiblingAndReplaysLisbonPricesAndEuroDividends(
        bool sharedIssuer
    )
    {
        var us = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME", Cik: "0000000991");
        us.Presentation.Listing.TradingCurrency = "USD";
        DbContext.Add(us);
        await DbContext.SaveChangesAsync();
        var lisbon = await SeedLisbon(sharedIssuer ? us : null);
        var usPrice = Price(us.Presentation.Listing, Session, 999);
        var previous = Price(lisbon, Session.AddDays(-1), 10);
        var originalDividend = new CashDividend
        {
            Issuer = us,
            Listing = us.Presentation.Listing,
            Currency = "USD",
            ExDate = Session,
            AmountPerShare = 9,
            Source = CashDividendSource.Yahoo,
        };
        DbContext.AddRange(usPrice, previous, originalDividend);
        await DbContext.SaveChangesAsync();
        UsMarketCalendar.IsTradingDay(Session).Should().BeFalse();
        _client.GetChart("SAME.LS", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(_ => Chart());
        var service = Service();
        var target = (await Targets(service)).Should().ContainSingle().Which;
        target.EquityListingId.Should().Be(lisbon.Id);
        target.IsPrimary.Should().BeFalse();

        await Import(service, target, Session.AddDays(1));
        var importedId = await DbContext
            .Set<EquityDailyStockPrice>()
            .Where(price => price.EquityListingId == lisbon.Id && price.Date == Session)
            .Select(price => price.Id)
            .SingleAsync();
        await Import(service, target, Session.AddDays(2));

        await using var read = Fixture.CreateDbContext();
        (await read.Set<EquityDailyStockPrice>().CountAsync()).Should().Be(3);
        (await read.Set<EquityDailyStockPrice>().SingleAsync(row => row.Id == importedId))
            .Close.Should()
            .Be(11);
        (await read.Set<EquityDailyStockPrice>().SingleAsync(row => row.Id == usPrice.Id))
            .Close.Should()
            .Be(999);
        var payments = await read.Set<CashDividend>().ToListAsync();
        payments.Should().HaveCount(2);
        payments.Single(row => row.EquityListingId == lisbon.Id).Currency.Should().Be("EUR");
        payments.Single(row => row.EquityListingId == lisbon.Id).AmountPerShare.Should().Be(.25m);
        payments.Single(row => row.Id == originalDividend.Id).AmountPerShare.Should().Be(9);
        var evidence = await read.Set<EquityDirectorySourceRecord>()
            .Where(row => row.Source == YahooListingSource.LisbonEvidenceSource)
            .ToListAsync();
        evidence
            .Should()
            .ContainSingle()
            .Which.PayloadJson.Should()
            .Contain("SAME.LS")
            .And.Contain("Europe/Lisbon");
        await _client.DidNotReceive().GetChart("SAME", Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("currency")]
    [InlineData("exchange")]
    [InlineData("timezone")]
    [InlineData("type")]
    [InlineData("missing")]
    public async Task Import_ConflictingChartIdentity_PreservesExistingPricesAndCapturesNoActions(
        string conflict
    )
    {
        var listing = await SeedLisbon();
        var original = Price(listing, Session.AddDays(-1), 10);
        DbContext.Add(original);
        await DbContext.SaveChangesAsync();
        var chart = Chart();
        switch (conflict)
        {
            case "symbol":
                chart.SourceIdentity.Symbol = "SAME";
                break;
            case "currency":
                chart.SourceIdentity.Currency = "USD";
                break;
            case "exchange":
                chart.SourceIdentity.ExchangeCode = "NMS";
                break;
            case "timezone":
                chart.SourceIdentity.ExchangeTimeZone = "America/New_York";
                break;
            case "type":
                chart.SourceIdentity.InstrumentType = "ETF";
                break;
            case "missing":
                chart.SourceIdentity = null;
                break;
        }
        chart.Splits.Add(
            new StockSplitEvent
            {
                Date = Session,
                Numerator = 2,
                Denominator = 1,
            }
        );
        _client.GetChart("SAME.LS", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);

        await Import(Service(), Target(listing), Session.AddDays(1));

        await using var read = Fixture.CreateDbContext();
        (await read.Set<EquityDailyStockPrice>().SingleAsync()).Id.Should().Be(original.Id);
        (await read.Set<CashDividend>().CountAsync()).Should().Be(0);
        (await read.Set<StockSplit>().CountAsync()).Should().Be(0);
        (await read.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Queue_AmbiguousLisbonVenueClaimBlocksImportUntilRetiredAndUnqualifiedFilterStaysUsOnly()
    {
        var listing = await SeedLisbon();
        var pending = new EquityListing
        {
            Security = listing.Security,
            Ticker = "SAME",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "ENXL",
        };
        DbContext.Add(pending);
        await DbContext.SaveChangesAsync();
        var service = Service();
        (await Targets(service)).Should().BeEmpty();
        await Import(service, Target(listing), Session.AddDays(1));
        await _client
            .DidNotReceive()
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
        pending.Active = false;
        await DbContext.SaveChangesAsync();
        _options.TickersToSync = ["SAME"];
        (await Targets(service)).Should().BeEmpty();
        _options.TickersToSync = ["XLIS:SAME"];
        (await Targets(service))
            .Should()
            .ContainSingle()
            .Which.EquityListingId.Should()
            .Be(listing.Id);
    }

    [Fact]
    public async Task Import_QuotationScaleChangesDuringFetch_RefusesEntireResponse()
    {
        var listing = await SeedLisbon();
        var original = Price(listing, Session.AddDays(-1), 10);
        DbContext.Add(original);
        await DbContext.SaveChangesAsync();
        _client
            .GetChart("SAME.LS", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(async _ =>
            {
                await using var directory = Fixture.CreateDbContext();
                await directory
                    .Set<EquityListing>()
                    .Where(row => row.Id == listing.Id)
                    .ExecuteUpdateAsync(set =>
                        set.SetProperty(row => row.QuoteUnitMultiplier, .01m)
                    );
                return Chart();
            });

        await Import(Service(), Target(listing), Session.AddDays(1));

        await using var read = Fixture.CreateDbContext();
        (await read.Set<EquityDailyStockPrice>().SingleAsync()).Id.Should().Be(original.Id);
        (await read.Set<CashDividend>().CountAsync()).Should().Be(0);
        (await read.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("scale")]
    [InlineData("mic")]
    [InlineData("isin")]
    [InlineData("verification")]
    [InlineData("country")]
    [InlineData("competing_claim")]
    public async Task IdentityChangesAfterQuotationCapture_RejectBothActionWrites(string change)
    {
        var listing = await SeedLisbon();
        var target = Target(listing);
        var service = Service();
        var chart = Chart();
        chart.Splits.Add(
            new StockSplitEvent
            {
                Date = Session,
                Numerator = 2,
                Denominator = 1,
            }
        );
        var quotation =
            (Task<bool>)
                typeof(YahooPriceImportService)
                    .GetMethod(
                        "CaptureQuotationBasis",
                        BindingFlags.NonPublic | BindingFlags.Instance
                    )!
                    .Invoke(service, [target, chart.SourceIdentity, CancellationToken.None])!;
        (await quotation).Should().BeTrue();

        await using (var changed = Fixture.CreateDbContext())
        {
            var current = await changed
                .Set<EquityListing>()
                .Include(row => row.Security)
                .SingleAsync(row => row.Id == listing.Id);
            switch (change)
            {
                case "scale":
                    current.QuoteUnitMultiplier = 0.01m;
                    break;
                case "mic":
                    current.MarketIdentifierCode = "ALXL";
                    break;
                case "isin":
                    current.Security.Isin = "PTEDP0AM0009";
                    break;
                case "verification":
                    current.IdentityState = EquityIdentityState.Legacy;
                    break;
                case "country":
                    current.MarketCountryCode = "GB";
                    break;
                case "competing_claim":
                    changed.Add(
                        new EquityListing
                        {
                            Ticker = listing.Ticker,
                            MarketCountryCode = "PT",
                            MarketIdentifierCode = "ENXL",
                            IdentityState = EquityIdentityState.Legacy,
                            Security = new EquitySecurity
                            {
                                Issuer = new EquityIssuer { Name = "Competing claimant" },
                            },
                        }
                    );
                    break;
            }
            await changed.SaveChangesAsync();
        }
        await (Task)
            typeof(YahooPriceImportService)
                .GetMethod("CaptureSplits", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(service, [target, chart.Splits, CancellationToken.None])!;
        await (Task)
            typeof(YahooPriceImportService)
                .GetMethod("CaptureDividends", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(service, [target, chart, CancellationToken.None])!;
        await using var read = Fixture.CreateDbContext();
        (await read.Set<StockSplit>().CountAsync()).Should().Be(0);
        (await read.Set<CashDividend>().CountAsync()).Should().Be(0);
        (await read.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FullHistory_ImportsWithoutIssuerPresentationAndReconcilesExactListingActions()
    {
        var listing = await SeedLisbon();
        var chart = Chart();
        chart.Prices.Insert(
            0,
            new HistoricalPrice
            {
                Date = new(2025, 7, 1),
                Open = 11,
                High = 11,
                Low = 11,
                Close = 11,
                AdjustedClose = 11,
                Volume = 100,
            }
        );
        chart.Splits.Add(
            new StockSplitEvent
            {
                Date = new(2025, 7, 2),
                Numerator = 2,
                Denominator = 1,
            }
        );
        _client.GetChart("SAME.LS", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chart);
        var service = Service();
        await Import(service, Target(listing), Session.AddDays(1));
        await (Task)
            typeof(YahooPriceImportService)
                .GetMethod(
                    "ReconcilePendingCorporateActions",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!
                .Invoke(service, [Session.AddDays(1), CancellationToken.None])!;

        await using var read = Fixture.CreateDbContext();
        (await read.Set<EquityIssuerPresentation>().CountAsync()).Should().Be(0);
        (await read.Set<CommonStock>().CountAsync()).Should().Be(0);
        (await read.Set<EquityDailyStockPrice>().CountAsync()).Should().Be(2);
        var split = await read.Set<StockSplit>().SingleAsync();
        split.EquityListingId.Should().Be(listing.Id);
        split.PriceAdjustmentAppliedTime.Should().NotBeNull();
        var dividend = await read.Set<CashDividend>().SingleAsync();
        dividend.EquityListingId.Should().Be(listing.Id);
        dividend.Currency.Should().Be("EUR");
        dividend.PriceAdjustmentAppliedTime.Should().NotBeNull();
    }
}

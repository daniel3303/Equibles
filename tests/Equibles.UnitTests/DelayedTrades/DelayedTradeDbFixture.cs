using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.DelayedTrades.Data;
using Equibles.DelayedTrades.Repositories;
using Equibles.EquityMarkets.Data;
using Equibles.EquityMarkets.Repositories;
using Equibles.TestSupport;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.DelayedTrades;

// One in-memory financial database with the modules the lane touches; composite keys need one shared model.
internal sealed class DelayedTradeDbFixture
{
    private static readonly IModuleConfiguration[] Modules =
    [
        new CommonStocksModuleConfiguration(),
        new YahooModuleConfiguration(),
        new EquityMarketsModuleConfiguration(),
        new DelayedTradesModuleConfiguration(),
    ];

    public DbContextOptions<EquiblesFinancialDbContext> Options { get; }
    public IServiceScopeFactory ScopeFactory { get; }
    public IServiceProvider Services { get; }

    public DelayedTradeDbFixture()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var prototype = new EquiblesFinancialDbContext(options, Modules);
        Options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>(options)
            .UseModel(prototype.Model)
            .Options;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext());
        services.AddScoped<EquityIssuerRepository>();
        services.AddScoped<EquityListingRepository>();
        services.AddScoped<EquityDailyStockPriceRepository>();
        services.AddScoped<LatestDelayedTradeRepository>();
        services.AddScoped<DelayedTradeImportPartitionRepository>();
        services.AddScoped<DelayedTradeFileCaptureRepository>();
        services.AddScoped<EquityMarketRegistrationRepository>();
        Services = services.BuildServiceProvider();
        ScopeFactory = Services.GetRequiredService<IServiceScopeFactory>();
    }

    public EquiblesFinancialDbContext NewContext()
    {
        var context = new EquiblesFinancialDbContext(Options, Modules);
        context.Database.EnsureCreated();
        return context;
    }

    public EquityIssuer SeedListing(
        string ticker,
        string isin,
        string mic = "XLIS",
        string country = "PT",
        string currency = "EUR",
        decimal? multiplier = 1m,
        EquityIdentityState state = EquityIdentityState.Verified,
        bool active = true
    )
    {
        var issuer = EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: ticker,
            Isin: isin,
            MarketCountryCode: country,
            MarketIdentifierCode: mic,
            IdentityState: state,
            TradingCurrency: currency,
            QuoteUnitMultiplier: multiplier,
            Active: active
        );
        using var context = NewContext();
        context.Add(issuer);
        context.SaveChanges();
        return issuer;
    }

    public EquityListing ListingOf(EquityIssuer issuer) =>
        issuer.Securities.SelectMany(security => security.Listings).Single();

    public DelayedTradeListingReference Reference(EquityIssuer issuer)
    {
        var listing = ListingOf(issuer);
        return new DelayedTradeListingReference(
            listing.Id,
            issuer.Id,
            listing.Security.Isin,
            listing.MarketIdentifierCode,
            listing.Ticker,
            listing.TradingCurrency,
            listing.QuoteUnitMultiplier,
            listing.IdentityState,
            listing.Active
        );
    }

    public EquityDailyStockPrice SeedPrice(
        EquityListing listing,
        DateOnly date,
        decimal close,
        string sourceTicker,
        long volume = 1_000,
        decimal? adjustedClose = null,
        decimal? open = null
    )
    {
        var row = new EquityDailyStockPrice
        {
            EquityListingId = listing.Id,
            SourceTicker = sourceTicker,
            Date = date,
            Open = open ?? close,
            High = Math.Max(open ?? close, close),
            Low = Math.Min(open ?? close, close),
            Close = close,
            AdjustedClose = adjustedClose ?? close,
            Volume = volume,
        };
        using var context = NewContext();
        context.Add(row);
        context.SaveChanges();
        return row;
    }

    public List<EquityDailyStockPrice> Prices(Guid listingId)
    {
        using var context = NewContext();
        return context
            .Set<EquityDailyStockPrice>()
            .Where(price => price.EquityListingId == listingId)
            .OrderBy(price => price.Date)
            .ToList();
    }
}

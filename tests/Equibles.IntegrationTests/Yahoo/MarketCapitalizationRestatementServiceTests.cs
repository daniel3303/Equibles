using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.EquityMarkets.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Data.Prices;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// The restatement pass over stored rows: which presented listings move to their newest traded
/// close and which stand as stored.
/// </summary>
public class MarketCapitalizationRestatementServiceTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly EquiblesFinancialDbContext _dbContext =
        TestDbContextFactory.CreateIgnoringInMemoryTransactionsWithoutPriceSeedDefaults(
            new EquityMarketsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );

    public void Dispose() => _dbContext.Dispose();

    private MarketCapitalizationRestatementService Service(
        params IVenueCapitalizationOwner[] owners
    ) =>
        new(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityListingRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            owners,
            Substitute.For<ILogger<MarketCapitalizationRestatementService>>()
        );

    // A pair read three days ago at that day's 1.51 close, with a newer 1.47 close stored since.
    private async Task<EquityIssuer> SeedStaleCap(string ticker, long shares = 166_404_266)
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: ticker,
            Cik: $"CIK-{ticker}"
        );
        var listing = stock.Presentation.Listing;
        listing.YahooEnrichmentAttemptedAt = DateTime.UtcNow.AddDays(-3);
        listing.Security.SharesOutstanding = shares;
        listing.Security.MarketCapitalization = shares * 1.51;
        _dbContext.Add(stock);
        _dbContext.AddRange(
            Bar(listing, Today.AddDays(-4), 1.50m, 1.55m, 1.51m),
            Bar(listing, Today.AddDays(-1), 1.43m, 1.55m, 1.47m)
        );
        await _dbContext.SaveChangesAsync();
        return stock;
    }

    private static EquityDailyStockPrice Bar(
        EquityListing listing,
        DateOnly date,
        decimal low,
        decimal high,
        decimal close
    ) =>
        new()
        {
            Listing = listing,
            SourceTicker = listing.Ticker,
            Date = date,
            Open = low,
            High = high,
            Low = low,
            Close = close,
            AdjustedClose = close,
            Volume = 50_000,
        };

    private double StoredCap(EquityIssuer stock) =>
        _dbContext
            .Set<EquitySecurity>()
            .Single(security => security.Id == stock.Presentation.Listing.EquitySecurityId)
            .MarketCapitalization;

    [Fact]
    public async Task Restate_CapReadAtAnOlderClose_MovesToTheNewestTradedClose()
    {
        var stock = await SeedStaleCap("HBFG");

        (await Service().Restate(CancellationToken.None)).Should().Be(1);

        StoredCap(stock).Should().BeApproximately(166_404_266 * 1.47, 0.5);
        (await Service().Restate(CancellationToken.None))
            .Should()
            .Be(0, "a second pass finds the cap on its close");
    }

    /// <summary>A zero-volume carry-forward candle is not a traded close and must not move the cap.</summary>
    [Fact]
    public async Task Restate_NewestBarHasNoVolume_KeepsTheLastTradedClose()
    {
        var stock = await SeedStaleCap("IDLE");
        var untraded = Bar(stock.Presentation.Listing, Today, 1.30m, 1.30m, 1.30m);
        untraded.Volume = 0;
        _dbContext.Add(untraded);
        await _dbContext.SaveChangesAsync();

        await Service().Restate(CancellationToken.None);

        StoredCap(stock).Should().BeApproximately(166_404_266 * 1.47, 0.5);
    }

    /// <summary>
    /// The count stored beside a split may predate it, so shares × the post-split close would be
    /// wrong by the ratio; the split reconciliation refreshes the pair from the feed instead.
    /// </summary>
    [Fact]
    public async Task Restate_SplitEffectiveSinceTheSnapshot_LeavesThePairToTheSplitRefresh()
    {
        var stock = await SeedStaleCap("SPLT");
        _dbContext.Add(
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EquityListingId = stock.Presentation.EquityListingId,
                EffectiveDate = Today.AddDays(-1),
                Numerator = 2m,
                Denominator = 1m,
                Source = StockSplitSource.External,
            }
        );
        await _dbContext.SaveChangesAsync();

        (await Service().Restate(CancellationToken.None)).Should().Be(0);

        StoredCap(stock).Should().Be(166_404_266 * 1.51);
    }

    [Fact]
    public async Task Restate_SecurityOwnedByAVenueLane_IsLeftToItsOwner()
    {
        var stock = await SeedStaleCap("NSEO");
        var owner = Substitute.For<IVenueCapitalizationOwner>();
        owner.Owns(Arg.Any<EquitySecurity>(), Arg.Any<CancellationToken>()).Returns(true);

        (await Service(owner).Restate(CancellationToken.None)).Should().Be(0);

        StoredCap(stock).Should().Be(166_404_266 * 1.51);
    }

    /// <summary>A cap that is no multiple of a traded price (a receipt counting ordinary shares) is never rewritten.</summary>
    [Fact]
    public async Task Restate_PairStatesNoTradedPrice_StandsAsStored()
    {
        var stock = await SeedStaleCap("ADSX");
        var security = stock.Presentation.Listing.Security;
        security.MarketCapitalization = security.SharesOutstanding * 1.51 * 13;
        await _dbContext.SaveChangesAsync();

        (await Service().Restate(CancellationToken.None)).Should().Be(0);

        StoredCap(stock).Should().Be(166_404_266 * 1.51 * 13);
    }
}

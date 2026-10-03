using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.TestSupport;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Prices;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins that the ownership filters and the US scope reach SQL in the null-safe shape they were
/// written in. <c>ToQueryString</c> runs the full Npgsql translation offline, so a provider or
/// refactor that turns the null guard into a bare NOT LIKE (which drops null-spelled rows) or
/// loses the market predicate fails here instead of in production.
/// </summary>
public class EquityDailyStockPriceRepositoryQueryTranslationTests
{
    private static EquiblesFinancialDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new YahooModuleConfiguration(),
            }
        );
    }

    [Fact]
    public void YahooOwned_TranslatesAsNullOrNoSeparator()
    {
        using var ctx = CreateContext();
        var sql = new EquityDailyStockPriceRepository(ctx)
            .GetAllSeries()
            .YahooOwned()
            .ToQueryString();

        sql.Should().Contain("\"SourceTicker\" IS NULL OR");
        sql.Should().Contain("NOT LIKE '%:%'");
    }

    [Fact]
    public void VenueOwned_TranslatesAsNotNullAndSeparator()
    {
        using var ctx = CreateContext();
        var sql = new EquityDailyStockPriceRepository(ctx)
            .GetAllSeries()
            .VenueOwned()
            .ToQueryString();

        sql.Should().Contain("\"SourceTicker\" IS NOT NULL AND");
        sql.Should().Contain("LIKE '%:%'");
        sql.Should().NotContain("NOT LIKE");
    }

    [Fact]
    public void GetPrimarySeries_TranslatesThePresentationJoinAndTheUsScope()
    {
        using var ctx = CreateContext();
        var sql = new EquityDailyStockPriceRepository(ctx).GetPrimarySeries().ToQueryString();

        sql.Should().Contain("\"EquityIssuerPresentation\"");
        sql.Should().Contain("\"MarketCountryCode\" = 'US'");
    }

    // The issuer-wide join could not use the (listing, date) index order, so PostgreSQL read
    // every bar and sorted before any LIMIT; the listing id from the loaded graph must reach SQL
    // as a plain column filter with no security join.
    [Fact]
    public void GetByStock_FiltersByThePresentationListingIdWithoutASecurityJoin()
    {
        using var ctx = CreateContext();
        var stock = EquityIssuerSeed.Create(Ticker: "AAPL");
        var repository = new EquityDailyStockPriceRepository(ctx);

        var series = repository.GetByStock(stock).ToQueryString();
        var window = repository
            .GetByStock(stock, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))
            .ToQueryString();
        var latest = repository.GetLatestDate(stock).ToQueryString();

        // EF names the already-joined listing alias; PostgreSQL carries that equality onto the
        // price table's listing column, so the (listing, date) index serves the read either way.
        foreach (var sql in new[] { series, window, latest })
        {
            sql.Should().MatchRegex("\"(EquityListingId|Id)\" = @listingId");
            sql.Should().NotContain("\"EquitySecurity\"");
            sql.Should().NotContain("\"EquityIssuerId\" = @");
        }
    }

    [Fact]
    public void GetByStock_ExactTicker_FiltersByTheOneListingIdWithoutTheSiblingProof()
    {
        using var ctx = CreateContext();
        var stock = EquityIssuerSeed.Create(Ticker: "BRK-B", SecondaryTickers: ["BRK-A"]);

        var sql = new EquityDailyStockPriceRepository(ctx)
            .GetByStock(stock, "BRK-A")
            .ToQueryString();

        sql.Should().MatchRegex("\"(EquityListingId|Id)\" = @listingId");
        sql.Should().NotContain("NOT EXISTS");
        sql.Should().NotContain("\"EquitySecurity\"");
    }
}

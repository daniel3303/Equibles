using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeQuotationEvidenceTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private static YahooChartSourceIdentity Source(string symbol = "AAPL") =>
        new()
        {
            Symbol = symbol,
            Currency = "USD",
            ExchangeCode = "NMS",
            ExchangeName = "NasdaqGS",
            InstrumentType = "EQUITY",
            ExchangeTimeZone = "America/New_York",
        };

    [Fact]
    public async Task Capture_UpdatesOnlyTheExactNativeListing_AndRetainsIdempotentEvidence()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "AAPL");
        var listing = issuer.Presentation.Listing;
        var foreign = new EquityListing
        {
            Security = listing.Security,
            Ticker = "AAPL",
            MarketIdentifierCode = "XLIS",
            MarketCountryCode = "PT",
        };
        DbContext.AddRange(issuer, foreign);
        await DbContext.SaveChangesAsync();
        var repository = new EquityListingRepository(DbContext);
        (await YahooQuotationIdentity.Capture(repository, issuer.Id, "AAPL", Source()))
            .Should()
            .BeTrue();
        (await YahooQuotationIdentity.Capture(repository, issuer.Id, "AAPL", Source()))
            .Should()
            .BeTrue();
        await DbContext.Entry(listing).ReloadAsync();
        await DbContext.Entry(foreign).ReloadAsync();
        listing.TradingCurrency.Should().Be("USD");
        listing.QuoteUnitMultiplier.Should().Be(1m);
        listing.MarketIdentifierCode.Should().BeNull();
        listing.IdentityState.Should().Be(EquityIdentityState.Legacy);
        foreign.TradingCurrency.Should().BeNull();
        foreign.QuoteUnitMultiplier.Should().BeNull();
        var evidence = await DbContext.Set<EquityDirectorySourceRecord>().SingleAsync();
        evidence.Source.Should().Be(YahooQuotationIdentity.Source);
        evidence.SourceRecordKey.Should().Be(listing.Id.ToString());
        evidence.PayloadJson.Should().Contain("NasdaqGS").And.Contain("America/New_York");
        evidence.PayloadHash.Should().HaveLength(64);
        (
            await DbContext
                .Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM pg_class WHERE oid = to_regclass('\"LegacyEquityListing\"')"
                )
                .SingleAsync()
        )
            .Should()
            .Be(0);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("retired")]
    [InlineData("ambiguous")]
    [InlineData("different-currency")]
    [InlineData("different-scale")]
    [InlineData("foreign")]
    [InlineData("renamed")]
    public async Task ConflictingOrUnavailableNativeIdentity_DoesNotMutateFactsOrCreateEvidence(
        string scenario
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "AAPL");
        var listing = issuer.Presentation.Listing;
        if (scenario == "retired")
            listing.Active = false;
        if (scenario == "different-currency")
            listing.TradingCurrency = "EUR";
        if (scenario == "different-scale")
            listing.QuoteUnitMultiplier = 0.01m;
        if (scenario == "foreign")
            listing.MarketCountryCode = "PT";
        if (scenario == "renamed")
            listing.Ticker = "NEW";
        if (scenario == "ambiguous")
            DbContext.Add(
                new EquityListing
                {
                    Security = listing.Security,
                    Ticker = "AAPL",
                    MarketCountryCode = "US",
                    MarketIdentifierCode = "XNYS",
                }
            );
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        var beforeCurrency = listing.TradingCurrency;
        var beforeScale = listing.QuoteUnitMultiplier;
        var owner = scenario == "wrong-owner" ? Guid.NewGuid() : issuer.Id;
        (
            await YahooQuotationIdentity.Capture(
                new EquityListingRepository(DbContext),
                owner,
                "AAPL",
                Source()
            )
        )
            .Should()
            .BeFalse();
        await DbContext.Entry(listing).ReloadAsync();
        listing.TradingCurrency.Should().Be(beforeCurrency);
        listing.QuoteUnitMultiplier.Should().Be(beforeScale);
        (await DbContext.Set<EquityDirectorySourceRecord>().CountAsync()).Should().Be(0);
    }
}

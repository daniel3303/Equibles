using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.CommonStocks;

// Contract: a bare ticker keeps its US meaning, MIC:TICKER names one verified venue listing, and a
// provider suffix is read only after the US lookup misses. US-only readers answer a venue symbol
// with the US-only notice rather than "not found".
public class ListingSymbolResolutionExtensionsTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly EquityIssuerRepository _repository;

    public ListingSymbolResolutionExtensionsTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new EquityIssuerRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    private EquityIssuer Venue(
        string ticker,
        string mic,
        string country,
        string name,
        EquityIdentityState state = EquityIdentityState.Verified
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: name,
            MarketCountryCode: country,
            MarketIdentifierCode: mic,
            IdentityState: state,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        _dbContext.Add(issuer);
        return issuer;
    }

    private EquityIssuer Us(string ticker, string name)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: name,
            Cik: Random.Shared.Next(1, 9_999_999).ToString()
        );
        _dbContext.Add(issuer);
        return issuer;
    }

    [Fact]
    public async Task BareTickerStaysUs_VenueSymbolReachesTheVenue()
    {
        Us("AIR", "AAR Corp");
        Venue("AIR", "XPAR", "FR", "Airbus");
        await _dbContext.SaveChangesAsync();

        var us = await _repository.ResolveListing("AIR");
        us.Stock.Name.Should().Be("AAR Corp");
        us.Venue.Should().BeNull();

        var paris = await _repository.ResolveListing(" xpar:air ");
        paris.Stock.Name.Should().Be("Airbus");
        EquityListingSymbol.Display(paris.Venue).Should().Be("XPAR:AIR");
        paris.Error.Should().BeNull();
    }

    [Fact]
    public async Task ProviderSuffix_ResolvesTheMarketOnlyAfterTheUsLookupMisses()
    {
        Venue("MC", "XPAR", "FR", "LVMH");
        Us("ABC-L", "US Class L");
        Venue("ABC", "XLON", "GB", "London ABC");
        await _dbContext.SaveChangesAsync();

        (await _repository.ResolveListing("MC.PA")).Stock.Name.Should().Be("LVMH");

        var usWins = await _repository.ResolveListing("ABC.L");
        usWins.Stock.Name.Should().Be("US Class L");
        usWins.Venue.Should().BeNull();
    }

    [Fact]
    public async Task NumericAndSeparatorSpellings_ReachTheStoredVenueTicker()
    {
        Venue("00005", "XHKG", "HK", "HSBC Holdings");
        Venue("BT-A", "XLON", "GB", "BT Group");
        await _dbContext.SaveChangesAsync();

        (await _repository.ResolveListing("XHKG:5")).Stock.Name.Should().Be("HSBC Holdings");
        (await _repository.ResolveListing("0005.HK")).Stock.Name.Should().Be("HSBC Holdings");
        (await _repository.ResolveListing("XLON:BT.A")).Stock.Name.Should().Be("BT Group");
    }

    [Fact]
    public async Task UnverifiedOrDuplicatedVenueClaims_FailClosed()
    {
        Venue("OLD", "XPAR", "FR", "Legacy", EquityIdentityState.Legacy);
        Venue("TWO", "XAMS", "NL", "First");
        Venue("TWO", "XAMS", "NL", "Second");
        await _dbContext.SaveChangesAsync();

        var legacy = await _repository.ResolveListing("XPAR:OLD");
        legacy.Stock.Should().BeNull();
        legacy.Error.Should().Contain("not found");

        var duplicate = await _repository.ResolveListing("XAMS:TWO");
        duplicate.Stock.Should().BeNull();
        duplicate.Error.Should().Contain("ambiguous");
    }

    [Fact]
    public async Task UsOnlyReaders_AnswerAVenueSymbolWithTheUsOnlyNotice()
    {
        Venue("AIR", "XPAR", "FR", "Airbus");
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("XPAR:AIR");
        stock.Should().BeNull();
        error.Should().Be(EquityListingSymbol.UsOnlyNotice("XPAR:AIR"));

        (await _repository.ResolveByTickerIncludingDelisted("XPAR:AIR"))
            .Error.Should()
            .Be(EquityListingSymbol.UsOnlyNotice("XPAR:AIR"));
    }

    [Fact]
    public async Task IssuerReaders_AcceptAVenueSymbol()
    {
        Venue("SAP", "XETR", "DE", "SAP SE");
        await _dbContext.SaveChangesAsync();

        (await _repository.ResolveIssuerIncludingDelisted("XETR:SAP"))
            .Stock.Name.Should()
            .Be("SAP SE");
        (await _repository.ResolveIssuerIncludingDelisted("SAP.DE"))
            .Stock.Name.Should()
            .Be("SAP SE");
        (await _repository.ResolveIssuerIncludingDelisted("SAP")).Stock.Should().BeNull();
    }
}

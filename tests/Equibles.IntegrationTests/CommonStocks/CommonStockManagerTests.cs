using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Exceptions;
using Equibles.IntegrationTests.Helpers;
using MassTransit;
using NSubstitute;

namespace Equibles.IntegrationTests.CommonStocks;

public class CommonStockManagerTests
{
    private readonly EquityIdentityManager _sut;
    private readonly EquityIssuerRepository _repository;

    public CommonStockManagerTests()
    {
        var context = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new EquityIssuerRepository(context);
        _sut = new EquityIdentityManager(_repository, Substitute.For<IBus>());
    }

    private static EquityIssuer MakeStock(
        string ticker = "AAPL",
        string name = "Apple Inc",
        string cik = "0000320193"
    )
    {
        return Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: ticker, Name: name, Cik: cik);
    }

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidStock_AddsAndReturns()
    {
        EquityIssuer stock = MakeStock();

        EquityIssuer result = await _sut.Create(stock);

        result.Should().BeSameAs(stock);
        EquityIssuer persisted = await _repository.GetUsByTicker("AAPL");
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_NullStock_ThrowsArgumentNullException()
    {
        var act = () => _sut.Create(null);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Create_EmptyTicker_ThrowsDomainValidationException()
    {
        EquityIssuer stock = MakeStock(ticker: "");

        var act = () => _sut.Create(stock);

        await act.Should()
            .ThrowAsync<DomainValidationException>()
            .WithMessage("Ticker is required");
    }

    [Fact]
    public async Task Create_UnlistedIssuer_PreservesItsIdentityWithoutInventingAListing()
    {
        var issuer = MakeStock(ticker: null);
        var created = await _sut.Create(issuer);
        created.Id.Should().Be(issuer.Id);
        created.Presentation.Should().BeNull();
        created.Securities.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_EmptyName_ThrowsDomainValidationException()
    {
        EquityIssuer stock = MakeStock(name: "");

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("Name is required");
    }

    [Fact]
    public async Task Create_EmptyCik_ThrowsDomainValidationException()
    {
        EquityIssuer stock = MakeStock(cik: "");

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("Cik is required");
    }

    [Fact]
    public async Task Create_NegativeMarketCap_ThrowsDomainValidationException()
    {
        EquityIssuer stock = MakeStock();
        stock.Presentation.Listing.Security.MarketCapitalization = -1;

        var act = () => _sut.Create(stock);

        await act.Should()
            .ThrowAsync<DomainValidationException>()
            .WithMessage("*cannot be negative*");
    }

    [Fact]
    public async Task Create_NegativeSharesOutstanding_ThrowsDomainValidationException()
    {
        EquityIssuer stock = MakeStock();
        stock.Presentation.Listing.Security.SharesOutstanding = -1;

        var act = () => _sut.Create(stock);

        await act.Should()
            .ThrowAsync<DomainValidationException>()
            .WithMessage("*cannot be negative*");
    }

    [Fact]
    public async Task Create_ZeroMarketCapAndShares_Succeeds()
    {
        EquityIssuer stock = MakeStock();
        stock.Presentation.Listing.Security.MarketCapitalization = 0;
        stock.Presentation.Listing.Security.SharesOutstanding = 0;

        EquityIssuer result = await _sut.Create(stock);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_DuplicateTicker_ThrowsDomainValidationException()
    {
        await _sut.Create(MakeStock());

        EquityIssuer duplicate = MakeStock(ticker: "AAPL", name: "Other", cik: "9999999");
        var act = () => _sut.Create(duplicate);

        await act.Should()
            .ThrowAsync<DomainValidationException>()
            .WithMessage("*ticker*already exists*");
    }

    [Fact]
    public async Task Create_DuplicateCik_ThrowsDomainValidationException()
    {
        await _sut.Create(MakeStock());

        EquityIssuer duplicate = MakeStock(ticker: "GOOG", name: "Other", cik: "0000320193");
        var act = () => _sut.Create(duplicate);

        await act.Should()
            .ThrowAsync<DomainValidationException>()
            .WithMessage("*cik*already exists*");
    }

    [Fact]
    public async Task Create_SecondaryTickerMatchesAnotherCompanyPrimary_Succeeds()
    {
        // SEC filings legitimately expose the same ticker on related filers (e.g. a REIT's
        // preferred-share class appearing on both the parent and the operating partnership).
        // The domain must accept a secondary ticker that is already primary on another company.
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));

        EquityIssuer stock = MakeStock(ticker: "GOOG", name: "Google", cik: "222");
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(stock, ["AAPL"]);

        EquityIssuer result = await _sut.Create(stock);

        result
            .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
            .Where(nativeListing =>
                nativeListing.MarketCountryCode == "US"
                && (
                    nativeListing.IsDirectoryListed
                    && nativeListing.Id != result.Presentation.EquityListingId
                )
            )
            .Select(nativeListing => nativeListing.Ticker)
            .ToList()
            .Should()
            .Contain("AAPL");
    }

    [Fact]
    public async Task Create_SecondaryTickerMatchesAnotherCompanySecondary_Succeeds()
    {
        EquityIssuer existing = MakeStock(ticker: "AAPL", name: "Apple", cik: "111");
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(existing, ["ALT1"]);
        await _sut.Create(existing);

        EquityIssuer stock = MakeStock(ticker: "GOOG", name: "Google", cik: "222");
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(stock, ["ALT1"]);

        EquityIssuer result = await _sut.Create(stock);

        result
            .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
            .Where(nativeListing =>
                nativeListing.MarketCountryCode == "US"
                && (
                    nativeListing.IsDirectoryListed
                    && nativeListing.Id != result.Presentation.EquityListingId
                )
            )
            .Select(nativeListing => nativeListing.Ticker)
            .ToList()
            .Should()
            .Contain("ALT1");
    }

    [Fact]
    public async Task Create_SecondaryTickerNoConflict_Succeeds()
    {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));

        EquityIssuer stock = MakeStock(ticker: "GOOG", name: "Google", cik: "222");
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(stock, ["GOOGL"]);

        EquityIssuer result = await _sut.Create(stock);

        result.Should().NotBeNull();
    }

    // ── Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ValidNoConflicts_Succeeds()
    {
        EquityIssuer stock = await _sut.Create(MakeStock());
        stock.Name = "Apple Inc Updated";

        EquityIssuer result = await _sut.Update(stock);

        result.Name.Should().Be("Apple Inc Updated");
    }

    [Fact]
    public async Task Update_NullStock_ThrowsArgumentNullException()
    {
        var act = () => _sut.Update(null);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Update_SameTickerAsSelf_Succeeds()
    {
        EquityIssuer stock = await _sut.Create(MakeStock());
        stock.Name = "Updated Name";

        EquityIssuer result = await _sut.Update(stock);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_SameCikAsSelf_Succeeds()
    {
        EquityIssuer stock = await _sut.Create(MakeStock());
        stock.Name = "Updated Name";

        EquityIssuer result = await _sut.Update(stock);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_TickerConflictWithDifferentStock_ThrowsDomainValidationException()
    {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        EquityIssuer google = await _sut.Create(
            MakeStock(ticker: "GOOG", name: "Google", cik: "222")
        );
        google.Presentation.Listing.Ticker = "AAPL";

        var act = () => _sut.Update(google);

        await act.Should()
            .ThrowAsync<DomainValidationException>()
            .WithMessage("*ticker*already exists*");
    }

    [Fact]
    public async Task Update_CikConflictWithDifferentStock_ThrowsDomainValidationException()
    {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        EquityIssuer google = await _sut.Create(
            MakeStock(ticker: "GOOG", name: "Google", cik: "222")
        );
        google.Cik = "111";

        var act = () => _sut.Update(google);

        await act.Should()
            .ThrowAsync<DomainValidationException>()
            .WithMessage("*cik*already exists*");
    }

    [Fact]
    public async Task Update_SecondaryTickerMatchesAnotherCompanyPrimary_Succeeds()
    {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        EquityIssuer google = await _sut.Create(
            MakeStock(ticker: "GOOG", name: "Google", cik: "222")
        );
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(google, ["AAPL"]);

        EquityIssuer result = await _sut.Update(google);

        result
            .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
            .Where(nativeListing =>
                nativeListing.MarketCountryCode == "US"
                && (
                    nativeListing.IsDirectoryListed
                    && nativeListing.Id != result.Presentation.EquityListingId
                )
            )
            .Select(nativeListing => nativeListing.Ticker)
            .ToList()
            .Should()
            .Contain("AAPL");
    }

    [Fact]
    public async Task Update_PrimaryTickerConflictWithAnotherCompanySecondary_Succeeds()
    {
        // A primary ticker that exists only as a secondary on another company is still free
        // for use as a new primary — only primary-vs-primary collisions are disallowed.
        EquityIssuer apple = await _sut.Create(
            MakeStock(ticker: "AAPL", name: "Apple", cik: "111")
        );
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(apple, ["LEGACY"]);
        await _sut.Update(apple);

        EquityIssuer google = await _sut.Create(
            MakeStock(ticker: "GOOG", name: "Google", cik: "222")
        );
        google.Presentation.Listing.Ticker = "LEGACY";

        EquityIssuer result = await _sut.Update(google);

        result.Presentation.Listing.Ticker.Should().Be("LEGACY");
    }

    [Fact]
    public async Task Update_SecondaryTickerSameCompany_Succeeds()
    {
        EquityIssuer stock = await _sut.Create(
            MakeStock(ticker: "AAPL", name: "Apple", cik: "111")
        );
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(stock, ["AAPL-OLD"]);

        EquityIssuer result = await _sut.Update(stock);

        result
            .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
            .Where(nativeListing =>
                nativeListing.MarketCountryCode == "US"
                && (
                    nativeListing.IsDirectoryListed
                    && nativeListing.Id != result.Presentation.EquityListingId
                )
            )
            .Select(nativeListing => nativeListing.Ticker)
            .ToList()
            .Should()
            .Contain("AAPL-OLD");
    }
}

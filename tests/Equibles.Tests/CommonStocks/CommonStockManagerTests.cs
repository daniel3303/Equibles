using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Exceptions;
using Equibles.Tests.Helpers;

namespace Equibles.Tests.CommonStocks;

public class CommonStockManagerTests {
    private readonly CommonStockManager _sut;
    private readonly CommonStockRepository _repository;

    public CommonStockManagerTests() {
        var context = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(context);
        _sut = new CommonStockManager(_repository);
    }

    private static CommonStock MakeStock(string ticker = "AAPL", string name = "Apple Inc", string cik = "0000320193") {
        return new CommonStock { Ticker = ticker, Name = name, Cik = cik };
    }

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidStock_AddsAndReturns() {
        var stock = MakeStock();

        var result = await _sut.Create(stock);

        result.Should().BeSameAs(stock);
        var persisted = await _repository.GetByTicker("AAPL");
        persisted.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_NullStock_ThrowsArgumentNullException() {
        var act = () => _sut.Create(null);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Create_EmptyTicker_ThrowsDomainValidationException() {
        var stock = MakeStock(ticker: "");

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("Ticker is required");
    }

    [Fact]
    public async Task Create_NullTicker_ThrowsDomainValidationException() {
        var stock = MakeStock(ticker: null);

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("Ticker is required");
    }

    [Fact]
    public async Task Create_EmptyName_ThrowsDomainValidationException() {
        var stock = MakeStock(name: "");

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("Name is required");
    }

    [Fact]
    public async Task Create_EmptyCik_ThrowsDomainValidationException() {
        var stock = MakeStock(cik: "");

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("Cik is required");
    }

    [Fact]
    public async Task Create_NegativeMarketCap_ThrowsDomainValidationException() {
        var stock = MakeStock();
        stock.MarketCapitalization = -1;

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public async Task Create_NegativeSharesOutstanding_ThrowsDomainValidationException() {
        var stock = MakeStock();
        stock.SharesOutStanding = -1;

        var act = () => _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public async Task Create_ZeroMarketCapAndShares_Succeeds() {
        var stock = MakeStock();
        stock.MarketCapitalization = 0;
        stock.SharesOutStanding = 0;

        var result = await _sut.Create(stock);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_DuplicateTicker_ThrowsDomainValidationException() {
        await _sut.Create(MakeStock());

        var duplicate = MakeStock(ticker: "AAPL", name: "Other", cik: "9999999");
        var act = () => _sut.Create(duplicate);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*ticker*already exists*");
    }

    [Fact]
    public async Task Create_DuplicateCik_ThrowsDomainValidationException() {
        await _sut.Create(MakeStock());

        var duplicate = MakeStock(ticker: "GOOG", name: "Other", cik: "0000320193");
        var act = () => _sut.Create(duplicate);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*cik*already exists*");
    }

    [Fact]
    public async Task Create_SecondaryTickerMatchesAnotherCompanyPrimary_Succeeds() {
        // SEC filings legitimately expose the same ticker on related filers (e.g. a REIT's
        // preferred-share class appearing on both the parent and the operating partnership).
        // The domain must accept a secondary ticker that is already primary on another company.
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));

        var stock = MakeStock(ticker: "GOOG", name: "Google", cik: "222");
        stock.SecondaryTickers = ["AAPL"];

        var result = await _sut.Create(stock);

        result.SecondaryTickers.Should().Contain("AAPL");
    }

    [Fact]
    public async Task Create_SecondaryTickerMatchesAnotherCompanySecondary_Succeeds() {
        var existing = MakeStock(ticker: "AAPL", name: "Apple", cik: "111");
        existing.SecondaryTickers = ["ALT1"];
        await _sut.Create(existing);

        var stock = MakeStock(ticker: "GOOG", name: "Google", cik: "222");
        stock.SecondaryTickers = ["ALT1"];

        var result = await _sut.Create(stock);

        result.SecondaryTickers.Should().Contain("ALT1");
    }

    [Fact]
    public async Task Create_SecondaryTickerNoConflict_Succeeds() {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));

        var stock = MakeStock(ticker: "GOOG", name: "Google", cik: "222");
        stock.SecondaryTickers = ["GOOGL"];

        var result = await _sut.Create(stock);

        result.Should().NotBeNull();
    }

    // ── Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ValidNoConflicts_Succeeds() {
        var stock = await _sut.Create(MakeStock());
        stock.Name = "Apple Inc Updated";

        var result = await _sut.Update(stock);

        result.Name.Should().Be("Apple Inc Updated");
    }

    [Fact]
    public async Task Update_NullStock_ThrowsArgumentNullException() {
        var act = () => _sut.Update(null);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Update_SameTickerAsSelf_Succeeds() {
        var stock = await _sut.Create(MakeStock());
        stock.Name = "Updated Name";

        var result = await _sut.Update(stock);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_SameCikAsSelf_Succeeds() {
        var stock = await _sut.Create(MakeStock());
        stock.Name = "Updated Name";

        var result = await _sut.Update(stock);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_TickerConflictWithDifferentStock_ThrowsDomainValidationException() {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        var google = await _sut.Create(MakeStock(ticker: "GOOG", name: "Google", cik: "222"));
        google.Ticker = "AAPL";

        var act = () => _sut.Update(google);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*ticker*already exists*");
    }

    [Fact]
    public async Task Update_CikConflictWithDifferentStock_ThrowsDomainValidationException() {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        var google = await _sut.Create(MakeStock(ticker: "GOOG", name: "Google", cik: "222"));
        google.Cik = "111";

        var act = () => _sut.Update(google);

        await act.Should().ThrowAsync<DomainValidationException>().WithMessage("*cik*already exists*");
    }

    [Fact]
    public async Task Update_SecondaryTickerMatchesAnotherCompanyPrimary_Succeeds() {
        await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        var google = await _sut.Create(MakeStock(ticker: "GOOG", name: "Google", cik: "222"));
        google.SecondaryTickers = ["AAPL"];

        var result = await _sut.Update(google);

        result.SecondaryTickers.Should().Contain("AAPL");
    }

    [Fact]
    public async Task Update_PrimaryTickerConflictWithAnotherCompanySecondary_Succeeds() {
        // A primary ticker that exists only as a secondary on another company is still free
        // for use as a new primary — only primary-vs-primary collisions are disallowed.
        var apple = await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        apple.SecondaryTickers = ["LEGACY"];
        await _sut.Update(apple);

        var google = await _sut.Create(MakeStock(ticker: "GOOG", name: "Google", cik: "222"));
        google.Ticker = "LEGACY";

        var result = await _sut.Update(google);

        result.Ticker.Should().Be("LEGACY");
    }

    [Fact]
    public async Task Update_SecondaryTickerSameCompany_Succeeds() {
        var stock = await _sut.Create(MakeStock(ticker: "AAPL", name: "Apple", cik: "111"));
        stock.SecondaryTickers = ["AAPL-OLD"];

        var result = await _sut.Update(stock);

        result.SecondaryTickers.Should().Contain("AAPL-OLD");
    }
}

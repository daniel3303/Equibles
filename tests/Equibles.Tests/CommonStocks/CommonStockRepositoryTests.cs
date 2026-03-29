using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.CommonStocks;

public class CommonStockRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CommonStockRepository _repository;

    public CommonStockRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static Industry MakeIndustry(string name = "Technology") {
        return new Industry { Id = Guid.NewGuid(), Name = name };
    }

    private static CommonStock MakeStock(
        string ticker = "AAPL",
        string name = "Apple Inc",
        string cik = "0000320193",
        string description = "Consumer electronics company",
        Industry industry = null,
        List<string> secondaryTickers = null) {
        var stock = new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = cik,
            Description = description,
            IndustryId = industry?.Id,
            Industry = industry,
        };

        if (secondaryTickers != null)
            stock.SecondaryTickers = secondaryTickers;

        return stock;
    }

    private async Task SeedStocks(params CommonStock[] stocks) {
        _dbContext.Set<CommonStock>().AddRange(stocks);
        await _dbContext.SaveChangesAsync();
    }

    // ── GetByCik ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCik_ExistingCik_ReturnsMatchingStock() {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "0000320193"),
            MakeStock(ticker: "MSFT", cik: "0000789019"));

        var result = await _repository.GetByCik("0000320193");

        result.Should().NotBeNull();
        result.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetByCik_NonExistentCik_ReturnsNull() {
        await SeedStocks(MakeStock());

        var result = await _repository.GetByCik("9999999999");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCik_EmptyDatabase_ReturnsNull() {
        var result = await _repository.GetByCik("0000320193");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCik_CikIsCaseSensitive_DoesNotMatchDifferentCase() {
        await SeedStocks(MakeStock(cik: "ABC123"));

        var result = await _repository.GetByCik("abc123");

        result.Should().BeNull();
    }

    // ── GetByCiks ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCiks_MultipleMatchingCiks_ReturnsAllMatches() {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333"));

        var result = _repository.GetByCiks(["111", "333"]).ToList();

        result.Should().HaveCount(2);
        result.Select(s => s.Ticker).Should().BeEquivalentTo(["AAPL", "GOOG"]);
    }

    [Fact]
    public async Task GetByCiks_NoneMatch_ReturnsEmpty() {
        await SeedStocks(MakeStock(cik: "111"));

        var result = _repository.GetByCiks(["999", "888"]).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCiks_EmptyInput_ReturnsEmpty() {
        await SeedStocks(MakeStock());

        var result = _repository.GetByCiks([]).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCiks_PartialMatch_ReturnsOnlyMatching() {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"));

        var result = _repository.GetByCiks(["111", "999"]).ToList();

        result.Should().ContainSingle()
            .Which.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public void GetByCiks_ReturnsIQueryable_SupportsChaining() {
        var result = _repository.GetByCiks(["111"]);

        result.Should().BeAssignableTo<IQueryable<CommonStock>>();
    }

    // ── GetByName ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByName_ExactMatch_ReturnsStock() {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        var result = await _repository.GetByName("Apple Inc");

        result.Should().NotBeNull();
        result.Name.Should().Be("Apple Inc");
    }

    [Fact]
    public async Task GetByName_CaseInsensitiveMatch_ReturnsStock() {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        var result = await _repository.GetByName("apple inc");

        result.Should().NotBeNull();
        result.Name.Should().Be("Apple Inc");
    }

    [Fact]
    public async Task GetByName_UpperCaseInput_ReturnsStock() {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        var result = await _repository.GetByName("APPLE INC");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByName_NoMatch_ReturnsNull() {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        var result = await _repository.GetByName("Microsoft");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_PartialNameDoesNotMatch() {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        var result = await _repository.GetByName("Apple");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_EmptyDatabase_ReturnsNull() {
        var result = await _repository.GetByName("Apple Inc");

        result.Should().BeNull();
    }

    // ── GetByTicker ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByTicker_PrimaryTickerMatch_ReturnsStock() {
        await SeedStocks(MakeStock(ticker: "AAPL"));

        var result = await _repository.GetByTicker("AAPL");

        result.Should().NotBeNull();
        result.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetByTicker_SecondaryTickerMatch_ReturnsStock() {
        await SeedStocks(MakeStock(ticker: "GOOG", secondaryTickers: ["GOOGL", "GOOG-A"]));

        var result = await _repository.GetByTicker("GOOGL");

        result.Should().NotBeNull();
        result.Ticker.Should().Be("GOOG");
    }

    [Fact]
    public async Task GetByTicker_NoMatchInPrimaryOrSecondary_ReturnsNull() {
        await SeedStocks(MakeStock(ticker: "AAPL", secondaryTickers: ["AAPL-OLD"]));

        var result = await _repository.GetByTicker("MSFT");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByTicker_EmptyDatabase_ReturnsNull() {
        var result = await _repository.GetByTicker("AAPL");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByTicker_StockWithNoSecondaryTickers_MatchesPrimaryOnly() {
        await SeedStocks(MakeStock(ticker: "AAPL"));

        var result = await _repository.GetByTicker("AAPL");

        result.Should().NotBeNull();
        result.SecondaryTickers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByTicker_MultipleStocks_ReturnsCorrectOne() {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333"));

        var result = await _repository.GetByTicker("MSFT");

        result.Should().NotBeNull();
        result.Ticker.Should().Be("MSFT");
    }

    // ── GetByTickers ────────────────────────────────────────────────────
    // Note: GetByTickers uses SecondaryTickers.Any() which involves
    // querying into a JSON array column. The in-memory provider cannot
    // translate this expression, so most tests document the provider
    // limitation. Full coverage requires integration tests against PostgreSQL.

    [Fact]
    public void GetByTickers_ReturnsIQueryable_SupportsChaining() {
        var result = _repository.GetByTickers(["AAPL"]);

        result.Should().BeAssignableTo<IQueryable<CommonStock>>();
    }

    [Fact]
    public async Task GetByTickers_WithSecondaryTickerQuery_ThrowsBecauseJsonArrayNotSupportedInMemory() {
        await SeedStocks(MakeStock(ticker: "AAPL"));

        var act = () => _repository.GetByTickers(["AAPL"]).ToList();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── GetAllTickers ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAllTickers_ReturnsAllPrimaryTickers() {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333"));

        var result = _repository.GetAllTickers().ToList();

        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(["AAPL", "MSFT", "GOOG"]);
    }

    [Fact]
    public void GetAllTickers_EmptyDatabase_ReturnsEmpty() {
        var result = _repository.GetAllTickers().ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllTickers_DoesNotIncludeSecondaryTickers() {
        await SeedStocks(MakeStock(ticker: "GOOG", secondaryTickers: ["GOOGL"]));

        var result = _repository.GetAllTickers().ToList();

        result.Should().ContainSingle().Which.Should().Be("GOOG");
    }

    [Fact]
    public void GetAllTickers_ReturnsIQueryableOfString() {
        var result = _repository.GetAllTickers();

        result.Should().BeAssignableTo<IQueryable<string>>();
    }

    // ── GetAllSecondaryTickers ──────────────────────────────────────────
    // Note: GetAllSecondaryTickers uses SelectMany over a JSON array
    // column (SecondaryTickers) and a .Count filter, which the in-memory
    // provider cannot translate. Full coverage requires integration tests
    // against PostgreSQL.

    [Fact]
    public void GetAllSecondaryTickers_ReturnsIQueryableOfString() {
        var result = _repository.GetAllSecondaryTickers();

        result.Should().BeAssignableTo<IQueryable<string>>();
    }

    [Fact]
    public void GetAllSecondaryTickers_ThrowsBecauseJsonArrayNotSupportedInMemory() {
        var act = () => _repository.GetAllSecondaryTickers().ToList();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Search ──────────────────────────────────────────────────────────
    // Note: Search uses EF.Functions.ILike which is PostgreSQL-specific
    // and not supported by the in-memory provider. These tests verify
    // that Search throws InvalidOperationException to document this
    // provider limitation. Full Search coverage requires an integration
    // test against a real PostgreSQL instance.

    [Fact]
    public async Task Search_WithSearchTerm_ThrowsBecauseILikeNotSupportedInMemory() {
        await SeedStocks(MakeStock());

        var act = () => _repository.Search("Apple").ToList();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Search_WithEmptyString_ReturnsAllStocks() {
        // When search is empty, the ILike branch is skipped entirely
        _dbContext.Set<CommonStock>().AddRange(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"));
        _dbContext.SaveChanges();

        var result = _repository.Search("").ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Search_WithNull_ReturnsAllStocks() {
        _dbContext.Set<CommonStock>().AddRange(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"));
        _dbContext.SaveChanges();

        var result = _repository.Search(null).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Search_WithEmptyString_ReturnsResultsOrderedByTicker() {
        _dbContext.Set<CommonStock>().AddRange(
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "GOOG", cik: "333"));
        _dbContext.SaveChanges();

        var result = _repository.Search("").ToList();

        result.Select(s => s.Ticker).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Search_EmptyDatabase_ReturnsEmpty() {
        var result = _repository.Search("").ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Search_ReturnsIQueryable() {
        var result = _repository.Search("");

        result.Should().BeAssignableTo<IQueryable<CommonStock>>();
    }

    // ── Inherited BaseRepository Methods ────────────────────────────────
    // Verifies that CommonStockRepository correctly inherits and delegates
    // to the BaseRepository operations on CommonStock entities.

    [Fact]
    public async Task Add_CommonStock_PersistsViaSave() {
        var stock = MakeStock();

        _repository.Add(stock);
        await _repository.SaveChanges();

        var persisted = await _repository.GetByTicker("AAPL");
        persisted.Should().NotBeNull();
        persisted.Name.Should().Be("Apple Inc");
    }

    [Fact]
    public async Task AddRange_CommonStocks_PersistsAll() {
        var stocks = new[] {
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
        };

        _repository.AddRange(stocks);
        await _repository.SaveChanges();

        _repository.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public async Task Get_ByGuidId_ReturnsCorrectStock() {
        var stock = MakeStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        var result = await _repository.Get(stock.Id);

        result.Should().NotBeNull();
        result.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetAll_ReturnsAllStocksAsQueryable() {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333"));

        var result = _repository.GetAll().ToList();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Update_ModifiesExistingStock() {
        var stock = MakeStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        stock.Name = "Apple Inc Updated";
        _repository.Update(stock);
        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        var updated = await _repository.Get(stock.Id);
        updated.Name.Should().Be("Apple Inc Updated");
    }

    [Fact]
    public async Task Delete_RemovesStockFromDatabase() {
        var stock = MakeStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        _repository.Delete(stock);
        await _repository.SaveChanges();

        _repository.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_Collection_RemovesMultipleStocks() {
        var stocks = new[] {
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333"),
        };
        _repository.AddRange(stocks);
        await _repository.SaveChanges();

        _repository.Delete(stocks.Take(2));
        await _repository.SaveChanges();

        _repository.GetAll().Should().ContainSingle()
            .Which.Ticker.Should().Be("GOOG");
    }

    // ── Industry Navigation ─────────────────────────────────────────────

    [Fact]
    public async Task GetByTicker_WithIndustry_LoadsNavigationProperty() {
        var industry = MakeIndustry("Technology");
        _dbContext.Set<Industry>().Add(industry);
        await SeedStocks(MakeStock(ticker: "AAPL", industry: industry));

        var result = await _repository.GetAll()
            .Include(s => s.Industry)
            .FirstOrDefaultAsync(s => s.Ticker == "AAPL");

        result.Should().NotBeNull();
        result.Industry.Should().NotBeNull();
        result.Industry.Name.Should().Be("Technology");
    }
}

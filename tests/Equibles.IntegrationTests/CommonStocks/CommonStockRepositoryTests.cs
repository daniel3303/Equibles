using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

public class CommonStockRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly EquityIssuerRepository _repository;

    public CommonStockRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new EquityIssuerRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static Industry MakeIndustry(string name = "Technology")
    {
        return new Industry { Id = Guid.NewGuid(), Name = name };
    }

    private static EquityIssuer MakeStock(
        string ticker = "AAPL",
        string name = "Apple Inc",
        string cik = "0000320193",
        string description = "Consumer electronics company",
        Industry industry = null,
        List<string> secondaryTickers = null
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: name,
            Cik: cik,
            Description: description,
            IndustryId: industry?.Id,
            Industry: industry
        );

        if (secondaryTickers != null)
            Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(stock, secondaryTickers);

        return stock;
    }

    private async Task SeedStocks(params EquityIssuer[] stocks)
    {
        _dbContext.Set<EquityIssuer>().AddRange(stocks);
        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAll_ExcludesInactiveRows_WhileHistoricalQueryRetainsThem()
    {
        EquityIssuer active = MakeStock(ticker: "LIVE", cik: "111");
        EquityIssuer delisted = MakeStock(ticker: "GONE", cik: "222");
        delisted.Presentation.Listing.Active = false;
        delisted.Presentation.Listing.DelistedOn = new DateOnly(2024, 6, 14);
        await SeedStocks(active, delisted);

        var liveDirectory = await _repository
            .GetCurrentUsDirectory()
            .Select(stock => stock.Presentation.Listing.Ticker)
            .ToListAsync();
        var retained = await _repository
            .GetAll()
            .Select(stock => stock.Presentation.Listing.Ticker)
            .ToListAsync();

        liveDirectory.Should().Equal("LIVE");
        retained.Should().BeEquivalentTo("LIVE", "GONE");
        (await _repository.GetCurrentUsDirectoryIssuer(delisted.Id)).Should().BeNull();
        (await _repository.Get(delisted.Id)).Should().BeSameAs(delisted);
    }

    // ── GetByCik ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCik_ExistingCik_ReturnsMatchingStock()
    {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "0000320193"),
            MakeStock(ticker: "MSFT", cik: "0000789019")
        );

        EquityIssuer result = await _repository.GetByCik("0000320193");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetByCik_NonExistentCik_ReturnsNull()
    {
        await SeedStocks(MakeStock());

        EquityIssuer result = await _repository.GetByCik("9999999999");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCik_EmptyDatabase_ReturnsNull()
    {
        EquityIssuer result = await _repository.GetByCik("0000320193");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByCik_CikIsCaseSensitive_DoesNotMatchDifferentCase()
    {
        await SeedStocks(MakeStock(cik: "ABC123"));

        EquityIssuer result = await _repository.GetByCik("abc123");

        result.Should().BeNull();
    }

    // ── GetByCiks ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByCiks_MultipleMatchingCiks_ReturnsAllMatches()
    {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333")
        );

        var result = _repository.GetByCiks(["111", "333"]).ToList();

        result.Should().HaveCount(2);
        result.Select(s => s.Presentation.Listing.Ticker).Should().BeEquivalentTo(["AAPL", "GOOG"]);
    }

    [Fact]
    public async Task GetByCiks_NoneMatch_ReturnsEmpty()
    {
        await SeedStocks(MakeStock(cik: "111"));

        var result = _repository.GetByCiks(["999", "888"]).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCiks_EmptyInput_ReturnsEmpty()
    {
        await SeedStocks(MakeStock());

        var result = _repository.GetByCiks([]).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCiks_PartialMatch_ReturnsOnlyMatching()
    {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222")
        );

        var result = _repository.GetByCiks(["111", "999"]).ToList();

        result.Should().ContainSingle().Which.Presentation.Listing.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public void GetByCiks_ReturnsIQueryable_SupportsChaining()
    {
        var result = _repository.GetByCiks(["111"]);

        result.Should().BeAssignableTo<IQueryable<EquityIssuer>>();
    }

    // ── GetByName ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByName_ExactMatch_ReturnsStock()
    {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        EquityIssuer result = await _repository.GetByName("Apple Inc");

        result.Should().NotBeNull();
        result.Name.Should().Be("Apple Inc");
    }

    [Fact]
    public async Task GetByName_CaseInsensitiveMatch_ReturnsStock()
    {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        EquityIssuer result = await _repository.GetByName("apple inc");

        result.Should().NotBeNull();
        result.Name.Should().Be("Apple Inc");
    }

    [Fact]
    public async Task GetByName_UpperCaseInput_ReturnsStock()
    {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        EquityIssuer result = await _repository.GetByName("APPLE INC");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByName_NoMatch_ReturnsNull()
    {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        EquityIssuer result = await _repository.GetByName("Microsoft");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_PartialNameDoesNotMatch()
    {
        await SeedStocks(MakeStock(name: "Apple Inc"));

        EquityIssuer result = await _repository.GetByName("Apple");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_EmptyDatabase_ReturnsNull()
    {
        EquityIssuer result = await _repository.GetByName("Apple Inc");

        result.Should().BeNull();
    }

    // ── GetByTicker ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByTicker_PrimaryTickerMatch_ReturnsStock()
    {
        await SeedStocks(MakeStock(ticker: "AAPL"));

        EquityIssuer result = await _repository.GetUsByTicker("AAPL");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetByTicker_SecondaryTickerMatch_ReturnsStock()
    {
        await SeedStocks(MakeStock(ticker: "GOOG", secondaryTickers: ["GOOGL", "GOOG-A"]));

        EquityIssuer result = await _repository.GetUsByTicker("GOOGL");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("GOOG");
    }

    [Fact]
    public async Task GetByTicker_NoMatchInPrimaryOrSecondary_ReturnsNull()
    {
        await SeedStocks(MakeStock(ticker: "AAPL", secondaryTickers: ["AAPL-OLD"]));

        EquityIssuer result = await _repository.GetUsByTicker("MSFT");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByTicker_EmptyDatabase_ReturnsNull()
    {
        EquityIssuer result = await _repository.GetUsByTicker("AAPL");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByTicker_StockWithNoSecondaryTickers_MatchesPrimaryOnly()
    {
        await SeedStocks(MakeStock(ticker: "AAPL"));

        EquityIssuer result = await _repository.GetUsByTicker("AAPL");

        result.Should().NotBeNull();
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
            .BeEmpty();
    }

    [Fact]
    public async Task GetByTicker_MultipleStocks_ReturnsCorrectOne()
    {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333")
        );

        EquityIssuer result = await _repository.GetUsByTicker("MSFT");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("MSFT");
    }

    [Fact]
    public async Task GetByTicker_AmbiguousBetweenPrimaryAndSecondary_PrefersPrimary()
    {
        await SeedStocks(
            MakeStock(ticker: "SOHOB", name: "Sotherly LP", cik: "1313536"),
            MakeStock(
                ticker: "SOHOO",
                name: "Sotherly Inc",
                cik: "1301236",
                secondaryTickers: ["SOHOB"]
            )
        );

        EquityIssuer result = await _repository.GetUsByTicker("SOHOB");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("SOHOB");
        result.Cik.Should().Be("1313536");
    }

    [Fact]
    public async Task GetByTicker_AmbiguousWithSecondaryHolderSeededFirst_StillPrefersPrimary()
    {
        // Mirror of the previous test with inverse seed order. Guards against a regression
        // where insertion order rather than the explicit OrderBy drives the result.
        await SeedStocks(
            MakeStock(
                ticker: "SOHOO",
                name: "Sotherly Inc",
                cik: "1301236",
                secondaryTickers: ["SOHOB"]
            ),
            MakeStock(ticker: "SOHOB", name: "Sotherly LP", cik: "1313536")
        );

        EquityIssuer result = await _repository.GetUsByTicker("SOHOB");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("SOHOB");
        result.Cik.Should().Be("1313536");
    }

    // ── GetByPrimaryTicker ──────────────────────────────────────────────

    [Fact]
    public async Task GetByPrimaryTicker_PrimaryMatch_ReturnsStock()
    {
        await SeedStocks(MakeStock(ticker: "AAPL"));

        EquityIssuer result = await _repository.GetPrimaryUsByTicker("AAPL");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetByPrimaryTicker_OnlySecondaryMatch_ReturnsNull()
    {
        await SeedStocks(MakeStock(ticker: "GOOG", secondaryTickers: ["GOOGL"]));

        EquityIssuer result = await _repository.GetPrimaryUsByTicker("GOOGL");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByPrimaryTicker_NoMatch_ReturnsNull()
    {
        await SeedStocks(MakeStock(ticker: "AAPL"));

        EquityIssuer result = await _repository.GetPrimaryUsByTicker("MSFT");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByPrimaryTicker_EmptyDatabase_ReturnsNull()
    {
        EquityIssuer result = await _repository.GetPrimaryUsByTicker("AAPL");

        result.Should().BeNull();
    }

    // ── GetByTickers ────────────────────────────────────────────────────
    // Note: GetByTickers uses SecondaryTickers.Any() which involves
    // querying into a JSON array column. The in-memory provider cannot
    // translate this expression, so most tests document the provider
    // limitation. Full coverage requires integration tests against PostgreSQL.

    [Fact]
    public void GetByTickers_ReturnsIQueryable_SupportsChaining()
    {
        var result = _repository.GetUsByTickers(["AAPL"]);

        result.Should().BeAssignableTo<IQueryable<EquityIssuer>>();
    }

    [Fact]
    public async Task GetByTickers_ResolvesSecondaryNativeListing()
    {
        var issuer = MakeStock(ticker: "PRIMARY");
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(issuer, ["SECONDARY"]);
        await SeedStocks(issuer);
        (await _repository.GetUsByTickers(["SECONDARY"]).SingleAsync()).Id.Should().Be(issuer.Id);
    }

    // ── GetAllTickers ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAllTickers_ReturnsAllPrimaryTickers()
    {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333")
        );

        var result = _repository.GetUsPrimaryTickers().ToList();

        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(["AAPL", "MSFT", "GOOG"]);
    }

    [Fact]
    public void GetAllTickers_EmptyDatabase_ReturnsEmpty()
    {
        var result = _repository.GetUsPrimaryTickers().ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllTickers_DoesNotIncludeSecondaryTickers()
    {
        await SeedStocks(MakeStock(ticker: "GOOG", secondaryTickers: ["GOOGL"]));

        var result = _repository.GetUsPrimaryTickers().ToList();

        result.Should().ContainSingle().Which.Should().Be("GOOG");
    }

    [Fact]
    public void GetAllTickers_ReturnsIQueryableOfString()
    {
        var result = _repository.GetUsPrimaryTickers();

        result.Should().BeAssignableTo<IQueryable<string>>();
    }

    // ── GetAllSecondaryCiks ─────────────────────────────────────────────
    // Sibling to GetAllSecondaryTickers: same SelectMany-over-JSON-array
    // shape, same in-memory provider limitation. The repository surfaces
    // it for SEC ingest paths that need to detect every CIK ever assigned
    // to a CommonStock (parent + co-registrant subs). A regression that
    // collapses the query (e.g. dropping the .Count > 0 filter, or
    // returning primary CIKs by mistake) would silently change downstream
    // behavior. Pin the query shape so a refactor that breaks LINQ
    // composition surfaces immediately as a different exception or no
    // exception at all.

    [Fact]
    public void GetAllSecondaryCiks_ThrowsBecauseJsonArrayNotSupportedInMemory()
    {
        var act = () => _repository.GetAllSecondaryCiks().ToList();

        act.Should().Throw<InvalidOperationException>();
    }

    // ── GetAllSecondaryTickers ──────────────────────────────────────────
    // Note: GetAllSecondaryTickers uses SelectMany over a JSON array
    // column (SecondaryTickers) and a .Count filter, which the in-memory
    // provider cannot translate. Full coverage requires integration tests
    // against PostgreSQL.

    [Fact]
    public void GetAllSecondaryTickers_ReturnsIQueryableOfString()
    {
        var result = _repository.GetUsSecondaryTickers();

        result.Should().BeAssignableTo<IQueryable<string>>();
    }

    [Fact]
    public void GetAllSecondaryTickers_EmptyDirectory_IsEmpty()
    {
        _repository.GetUsSecondaryTickers().Should().BeEmpty();
    }

    // ── Search ──────────────────────────────────────────────────────────
    // Note: Search uses EF.Functions.ILike which is PostgreSQL-specific
    // and not supported by the in-memory provider. These tests verify
    // that Search throws InvalidOperationException to document this
    // provider limitation. Full Search coverage requires an integration
    // test against a real PostgreSQL instance.

    [Fact]
    public async Task Search_WithSearchTerm_ThrowsBecauseILikeNotSupportedInMemory()
    {
        await SeedStocks(MakeStock());

        var act = () => _repository.Search("Apple").ToList();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Search_WithEmptyString_ReturnsAllStocks()
    {
        // When search is empty, the ILike branch is skipped entirely
        _dbContext
            .Set<EquityIssuer>()
            .AddRange(MakeStock(ticker: "AAPL", cik: "111"), MakeStock(ticker: "MSFT", cik: "222"));
        _dbContext.SaveChanges();

        var result = _repository.Search("").ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Search_DefaultUniverse_ExcludesRetainedDelistedIdentities()
    {
        EquityIssuer live = MakeStock(ticker: "LIVE", cik: "111");
        EquityIssuer delisted = MakeStock(ticker: "GONE", cik: "222");
        delisted.Presentation.Listing.Active = false;
        _dbContext.Set<EquityIssuer>().AddRange(live, delisted);
        _dbContext.SaveChanges();

        var readers = _repository.Search("").Select(s => s.Presentation.Listing.Ticker).ToList();
        var operators = _repository
            .Search("", includeInactive: true)
            .Select(s => s.Presentation.Listing.Ticker)
            .ToList();

        readers.Should().Equal("LIVE");
        operators.Should().Equal("GONE", "LIVE");
    }

    [Fact]
    public void Search_WithNull_ReturnsAllStocks()
    {
        _dbContext
            .Set<EquityIssuer>()
            .AddRange(MakeStock(ticker: "AAPL", cik: "111"), MakeStock(ticker: "MSFT", cik: "222"));
        _dbContext.SaveChanges();

        var result = _repository.Search(null).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Search_WithEmptyString_ReturnsResultsOrderedByTicker()
    {
        _dbContext
            .Set<EquityIssuer>()
            .AddRange(
                MakeStock(ticker: "MSFT", cik: "222"),
                MakeStock(ticker: "AAPL", cik: "111"),
                MakeStock(ticker: "GOOG", cik: "333")
            );
        _dbContext.SaveChanges();

        var result = _repository.Search("").ToList();

        result.Select(s => s.Presentation.Listing.Ticker).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Search_EmptyDatabase_ReturnsEmpty()
    {
        var result = _repository.Search("").ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Search_ReturnsIQueryable()
    {
        var result = _repository.Search("");

        result.Should().BeAssignableTo<IQueryable<EquityIssuer>>();
    }

    // ── Inherited BaseRepository Methods ────────────────────────────────
    // Verifies that CommonStockRepository correctly inherits and delegates
    // to the BaseRepository operations on CommonStock entities.

    [Fact]
    public async Task Add_CommonStock_PersistsViaSave()
    {
        EquityIssuer stock = MakeStock();

        _repository.Add(stock);
        await _repository.SaveChanges();

        EquityIssuer persisted = await _repository.GetUsByTicker("AAPL");
        persisted.Should().NotBeNull();
        persisted.Name.Should().Be("Apple Inc");
    }

    [Fact]
    public async Task AddRange_CommonStocks_PersistsAll()
    {
        var stocks = new[]
        {
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
        };

        _repository.AddRange(stocks);
        await _repository.SaveChanges();

        _repository.GetCurrentUsDirectory().Should().HaveCount(2);
    }

    [Fact]
    public async Task Get_ByGuidId_ReturnsCorrectStock()
    {
        EquityIssuer stock = MakeStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        EquityIssuer result = await _repository.GetCurrentUsDirectoryIssuer(stock.Id);

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetAll_ReturnsAllStocksAsQueryable()
    {
        await SeedStocks(
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333")
        );

        var result = _repository.GetCurrentUsDirectory().ToList();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Update_ModifiesExistingStock()
    {
        EquityIssuer stock = MakeStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        stock.Name = "Apple Inc Updated";
        _repository.Update(stock);
        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        EquityIssuer updated = await _repository.GetCurrentUsDirectoryIssuer(stock.Id);
        updated.Name.Should().Be("Apple Inc Updated");
    }

    [Fact]
    public async Task Delete_IssuerWithListings_RefusesCascadeLoss()
    {
        EquityIssuer stock = MakeStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        var remove = () => _repository.Delete(stock);
        remove.Should().Throw<InvalidOperationException>();
        _repository.ClearChangeTracker();
        (await _repository.Get(stock.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_CollectionWithListings_PreservesEveryIssuer()
    {
        var stocks = new[]
        {
            MakeStock(ticker: "AAPL", cik: "111"),
            MakeStock(ticker: "MSFT", cik: "222"),
            MakeStock(ticker: "GOOG", cik: "333"),
        };
        _repository.AddRange(stocks);
        await _repository.SaveChanges();

        var remove = () => _repository.Delete(stocks.Take(2));
        remove.Should().Throw<InvalidOperationException>();
        _repository.ClearChangeTracker();
        _repository.GetAll().Should().HaveCount(3);
    }

    // ── Industry Navigation ─────────────────────────────────────────────

    [Fact]
    public async Task GetByTicker_WithIndustry_LoadsNavigationProperty()
    {
        var industry = MakeIndustry("Technology");
        _dbContext.Set<Industry>().Add(industry);
        await SeedStocks(MakeStock(ticker: "AAPL", industry: industry));

        EquityIssuer result = await _repository
            .GetCurrentUsDirectory()
            .Include(s => s.Industry)
            .FirstOrDefaultAsync(s => s.Presentation.Listing.Ticker == "AAPL");

        result.Should().NotBeNull();
        result.Industry.Should().NotBeNull();
        result.Industry.Name.Should().Be("Technology");
    }

    [Fact]
    public async Task GetByAnyCik_PrimaryCikMatch_ReturnsStock()
    {
        _dbContext.Set<EquityIssuer>().Add(MakeStock(ticker: "AAA", cik: "0000000001"));
        await _dbContext.SaveChangesAsync();

        EquityIssuer result = await _repository.GetByAnyCik("0000000001");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("AAA");
    }

    [Fact]
    public async Task GetByAnyCik_SecondaryCikMatch_ReturnsStock()
    {
        EquityIssuer stock = MakeStock(ticker: "BBB", cik: "0000000002");
        stock.SecondaryCiks = ["0000000099"];
        _dbContext.Set<EquityIssuer>().Add(stock);
        await _dbContext.SaveChangesAsync();

        EquityIssuer result = await _repository.GetByAnyCik("0000000099");

        result.Should().NotBeNull();
        result.Presentation.Listing.Ticker.Should().Be("BBB");
    }

    [Fact]
    public async Task GetByAnyCik_PrimaryAndSecondaryBothMatch_PrefersPrimary()
    {
        EquityIssuer primary = MakeStock(ticker: "PRIM", cik: "0000000050");
        EquityIssuer holder = MakeStock(ticker: "HOLD", cik: "0000000060");
        holder.SecondaryCiks = ["0000000050"];
        _dbContext.Set<EquityIssuer>().AddRange(primary, holder);
        await _dbContext.SaveChangesAsync();

        EquityIssuer result = await _repository.GetByAnyCik("0000000050");

        result.Should().NotBeNull();
        result.Cik.Should().Be("0000000050", "the primary-CIK match is ordered first");
    }

    [Fact]
    public async Task GetByAnyCik_NoMatch_ReturnsNull()
    {
        _dbContext.Set<EquityIssuer>().Add(MakeStock(ticker: "CCC", cik: "0000000003"));
        await _dbContext.SaveChangesAsync();

        EquityIssuer result = await _repository.GetByAnyCik("9999999999");

        result.Should().BeNull();
    }
}

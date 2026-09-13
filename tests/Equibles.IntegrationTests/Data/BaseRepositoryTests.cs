using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Data;

public class BaseRepositoryTests : IDisposable
{
    private sealed class TestRepository : BaseRepository<EquityIssuer>
    {
        public TestRepository(EquiblesFinancialDbContext dbContext)
            : base(dbContext) { }

        public DbSet<EquityIssuer> ExposeGetDbSet() => GetDbSet();

        public EquiblesFinancialDbContext ExposeGetDbContext() => GetDbContext();
    }

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly TestRepository _repository;

    public BaseRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new TestRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static EquityIssuer CreateStock(string ticker = "AAPL", string name = "Apple Inc.")
    {
        return Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: name
        );
    }

    // ── Get ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ExistingEntity_ReturnsEntity()
    {
        EquityIssuer stock = CreateStock();
        _dbContext.Set<EquityIssuer>().Add(stock);
        await _dbContext.SaveChangesAsync();

        EquityIssuer result = await _repository.Get(stock.Id);

        result.Should().NotBeNull();
        result.Id.Should().Be(stock.Id);
        result.Presentation.Listing.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task Get_NonExistentKey_ReturnsNull()
    {
        EquityIssuer result = await _repository.Get(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── GetAll ──────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_EmptySet_ReturnsEmptyQueryable()
    {
        var result = _repository.GetAll();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_WithEntities_ReturnsAllAsQueryable()
    {
        _dbContext
            .Set<EquityIssuer>()
            .AddRange(
                CreateStock("AAPL", "Apple"),
                CreateStock("MSFT", "Microsoft"),
                CreateStock("GOOG", "Alphabet")
            );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetAll();

        result.Should().HaveCount(3);
        result.Should().BeAssignableTo<IQueryable<EquityIssuer>>();
    }

    [Fact]
    public async Task GetAll_SupportsLinqFiltering()
    {
        _dbContext
            .Set<EquityIssuer>()
            .AddRange(CreateStock("AAPL", "Apple"), CreateStock("MSFT", "Microsoft"));
        await _dbContext.SaveChangesAsync();

        var result = _repository
            .GetAll()
            .Where(s => s.Presentation.Listing.Ticker == "MSFT")
            .ToList();

        result.Should().ContainSingle().Which.Name.Should().Be("Microsoft");
    }

    // ── Add ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_SingleEntity_PersistsAfterSave()
    {
        EquityIssuer stock = CreateStock();

        EquityIssuer returned = _repository.Add(stock);
        await _repository.SaveChanges();

        returned.Should().BeSameAs(stock);
        _dbContext
            .Set<EquityIssuer>()
            .Should()
            .ContainSingle()
            .Which.Presentation.Listing.Ticker.Should()
            .Be("AAPL");
    }

    [Fact]
    public async Task Add_ReturnsTheSameEntity()
    {
        EquityIssuer stock = CreateStock();

        EquityIssuer result = _repository.Add(stock);

        result.Should().BeSameAs(stock);
    }

    // ── AddRange ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRange_MultipleEntities_PersistsAllAfterSave()
    {
        var stocks = new[]
        {
            CreateStock("AAPL", "Apple"),
            CreateStock("MSFT", "Microsoft"),
            CreateStock("GOOG", "Alphabet"),
        };

        _repository.AddRange(stocks);
        await _repository.SaveChanges();

        _dbContext.Set<EquityIssuer>().Should().HaveCount(3);
    }

    [Fact]
    public async Task AddRange_EmptyCollection_NoEntitiesAdded()
    {
        _repository.AddRange([]);
        await _repository.SaveChanges();

        _dbContext.Set<EquityIssuer>().Should().BeEmpty();
    }

    // ── Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ModifiedEntity_PersistsChanges()
    {
        EquityIssuer stock = CreateStock();
        _dbContext.Set<EquityIssuer>().Add(stock);
        await _dbContext.SaveChangesAsync();

        stock.Name = "Apple Inc. (Updated)";
        _repository.Update(stock);
        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        EquityIssuer updated = await _repository.Get(stock.Id);
        updated.Name.Should().Be("Apple Inc. (Updated)");
    }

    // ── Delete (single) ─────────────────────────────────────────────────

    [Fact]
    public async Task Delete_SingleEntity_RemovesFromDatabase()
    {
        EquityIssuer stock = new() { Name = "Apple" };
        _dbContext.Set<EquityIssuer>().Add(stock);
        await _dbContext.SaveChangesAsync();

        _repository.Delete(stock);
        await _repository.SaveChanges();

        _dbContext.Set<EquityIssuer>().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_SingleEntity_DoesNotAffectOthers()
    {
        EquityIssuer apple = new() { Name = "Apple" };
        EquityIssuer msft = new() { Name = "Microsoft" };
        _dbContext.Set<EquityIssuer>().AddRange(apple, msft);
        await _dbContext.SaveChangesAsync();

        _repository.Delete(apple);
        await _repository.SaveChanges();

        _dbContext.Set<EquityIssuer>().Should().ContainSingle().Which.Name.Should().Be("Microsoft");
    }

    // ── Delete (collection) ─────────────────────────────────────────────

    [Fact]
    public async Task Delete_Collection_RemovesAllSpecifiedEntities()
    {
        var stocks = new[]
        {
            new EquityIssuer { Name = "Apple" },
            new EquityIssuer { Name = "Microsoft" },
            new EquityIssuer { Name = "Alphabet" },
        };
        _dbContext.Set<EquityIssuer>().AddRange(stocks);
        await _dbContext.SaveChangesAsync();

        _repository.Delete(stocks.Take(2));
        await _repository.SaveChanges();

        _dbContext.Set<EquityIssuer>().Should().ContainSingle().Which.Name.Should().Be("Alphabet");
    }

    [Fact]
    public async Task Delete_EmptyCollection_NoEntitiesRemoved()
    {
        EquityIssuer stock = new() { Name = "Apple" };
        _dbContext.Set<EquityIssuer>().Add(stock);
        await _dbContext.SaveChangesAsync();

        _repository.Delete([]);
        await _repository.SaveChanges();

        _dbContext.Set<EquityIssuer>().Should().ContainSingle();
    }

    // ── GetDbSet ────────────────────────────────────────────────────────

    [Fact]
    public void GetDbSet_ReturnsDbSetForEntity()
    {
        var dbSet = _repository.ExposeGetDbSet();

        dbSet.Should().NotBeNull();
        dbSet.Should().BeAssignableTo<DbSet<EquityIssuer>>();
    }

    [Fact]
    public async Task GetDbSet_ReturnsSameSetUsedByRepository()
    {
        EquityIssuer stock = CreateStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        _repository.ExposeGetDbSet().Should().ContainSingle().Which.Id.Should().Be(stock.Id);
    }

    // ── GetDbContext ────────────────────────────────────────────────────

    [Fact]
    public void GetDbContext_ReturnsInjectedContext()
    {
        var context = _repository.ExposeGetDbContext();

        context.Should().BeSameAs(_dbContext);
    }

    // ── ClearChangeTracker ──────────────────────────────────────────────

    [Fact]
    public void ClearChangeTracker_DetachesAllTrackedEntities()
    {
        EquityIssuer stock = CreateStock();
        _dbContext.Set<EquityIssuer>().Add(stock);

        _dbContext.ChangeTracker.Entries().Should().NotBeEmpty();

        _repository.ClearChangeTracker();

        _dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    // ── SaveChanges ─────────────────────────────────────────────────────

    [Fact]
    public async Task SaveChanges_PersistsTrackedChanges()
    {
        _repository.Add(CreateStock());

        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        _repository.GetAll().Should().ContainSingle();
    }

    [Fact]
    public async Task SaveChanges_WithoutChanges_DoesNotThrow()
    {
        var act = async () => await _repository.SaveChanges();

        await act.Should().NotThrowAsync();
    }

    // ── HasActiveTransaction ────────────────────────────────────────────

    [Fact]
    public void HasActiveTransaction_NoTransaction_ReturnsFalse()
    {
        _repository.HasActiveTransaction().Should().BeFalse();
    }
}

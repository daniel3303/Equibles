using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Data;

public class BaseRepositoryTests : IDisposable {
    private sealed class TestRepository : BaseRepository<CommonStock> {
        public TestRepository(EquiblesDbContext dbContext) : base(dbContext) { }

        public DbSet<CommonStock> ExposeGetDbSet() => GetDbSet();
        public EquiblesDbContext ExposeGetDbContext() => GetDbContext();
    }

    private readonly EquiblesDbContext _dbContext;
    private readonly TestRepository _repository;

    public BaseRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new TestRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
        };
    }

    // ── Get ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ExistingEntity_ReturnsEntity() {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.Get(stock.Id);

        result.Should().NotBeNull();
        result.Id.Should().Be(stock.Id);
        result.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task Get_NonExistentKey_ReturnsNull() {
        var result = await _repository.Get(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── GetAll ──────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_EmptySet_ReturnsEmptyQueryable() {
        var result = _repository.GetAll();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_WithEntities_ReturnsAllAsQueryable() {
        _dbContext.Set<CommonStock>().AddRange(
            CreateStock("AAPL", "Apple"),
            CreateStock("MSFT", "Microsoft"),
            CreateStock("GOOG", "Alphabet")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetAll();

        result.Should().HaveCount(3);
        result.Should().BeAssignableTo<IQueryable<CommonStock>>();
    }

    [Fact]
    public async Task GetAll_SupportsLinqFiltering() {
        _dbContext.Set<CommonStock>().AddRange(
            CreateStock("AAPL", "Apple"),
            CreateStock("MSFT", "Microsoft")
        );
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetAll().Where(s => s.Ticker == "MSFT").ToList();

        result.Should().ContainSingle()
            .Which.Name.Should().Be("Microsoft");
    }

    // ── Add ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_SingleEntity_PersistsAfterSave() {
        var stock = CreateStock();

        var returned = _repository.Add(stock);
        await _repository.SaveChanges();

        returned.Should().BeSameAs(stock);
        _dbContext.Set<CommonStock>().Should().ContainSingle()
            .Which.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task Add_ReturnsTheSameEntity() {
        var stock = CreateStock();

        var result = _repository.Add(stock);

        result.Should().BeSameAs(stock);
    }

    // ── AddRange ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRange_MultipleEntities_PersistsAllAfterSave() {
        var stocks = new[] {
            CreateStock("AAPL", "Apple"),
            CreateStock("MSFT", "Microsoft"),
            CreateStock("GOOG", "Alphabet"),
        };

        _repository.AddRange(stocks);
        await _repository.SaveChanges();

        _dbContext.Set<CommonStock>().Should().HaveCount(3);
    }

    [Fact]
    public async Task AddRange_EmptyCollection_NoEntitiesAdded() {
        _repository.AddRange([]);
        await _repository.SaveChanges();

        _dbContext.Set<CommonStock>().Should().BeEmpty();
    }

    // ── Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ModifiedEntity_PersistsChanges() {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        stock.Name = "Apple Inc. (Updated)";
        _repository.Update(stock);
        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        var updated = await _repository.Get(stock.Id);
        updated.Name.Should().Be("Apple Inc. (Updated)");
    }

    // ── Delete (single) ─────────────────────────────────────────────────

    [Fact]
    public async Task Delete_SingleEntity_RemovesFromDatabase() {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        _repository.Delete(stock);
        await _repository.SaveChanges();

        _dbContext.Set<CommonStock>().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_SingleEntity_DoesNotAffectOthers() {
        var apple = CreateStock("AAPL", "Apple");
        var msft = CreateStock("MSFT", "Microsoft");
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        await _dbContext.SaveChangesAsync();

        _repository.Delete(apple);
        await _repository.SaveChanges();

        _dbContext.Set<CommonStock>().Should().ContainSingle()
            .Which.Ticker.Should().Be("MSFT");
    }

    // ── Delete (collection) ─────────────────────────────────────────────

    [Fact]
    public async Task Delete_Collection_RemovesAllSpecifiedEntities() {
        var stocks = new[] {
            CreateStock("AAPL", "Apple"),
            CreateStock("MSFT", "Microsoft"),
            CreateStock("GOOG", "Alphabet"),
        };
        _dbContext.Set<CommonStock>().AddRange(stocks);
        await _dbContext.SaveChangesAsync();

        _repository.Delete(stocks.Take(2));
        await _repository.SaveChanges();

        _dbContext.Set<CommonStock>().Should().ContainSingle()
            .Which.Ticker.Should().Be("GOOG");
    }

    [Fact]
    public async Task Delete_EmptyCollection_NoEntitiesRemoved() {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        _repository.Delete([]);
        await _repository.SaveChanges();

        _dbContext.Set<CommonStock>().Should().ContainSingle();
    }

    // ── GetDbSet ────────────────────────────────────────────────────────

    [Fact]
    public void GetDbSet_ReturnsDbSetForEntity() {
        var dbSet = _repository.ExposeGetDbSet();

        dbSet.Should().NotBeNull();
        dbSet.Should().BeAssignableTo<DbSet<CommonStock>>();
    }

    [Fact]
    public async Task GetDbSet_ReturnsSameSetUsedByRepository() {
        var stock = CreateStock();
        _repository.Add(stock);
        await _repository.SaveChanges();

        _repository.ExposeGetDbSet().Should().ContainSingle()
            .Which.Id.Should().Be(stock.Id);
    }

    // ── GetDbContext ────────────────────────────────────────────────────

    [Fact]
    public void GetDbContext_ReturnsInjectedContext() {
        var context = _repository.ExposeGetDbContext();

        context.Should().BeSameAs(_dbContext);
    }

    // ── ClearChangeTracker ──────────────────────────────────────────────

    [Fact]
    public void ClearChangeTracker_DetachesAllTrackedEntities() {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);

        _dbContext.ChangeTracker.Entries().Should().NotBeEmpty();

        _repository.ClearChangeTracker();

        _dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    // ── SaveChanges ─────────────────────────────────────────────────────

    [Fact]
    public async Task SaveChanges_PersistsTrackedChanges() {
        _repository.Add(CreateStock());

        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        _repository.GetAll().Should().ContainSingle();
    }

    [Fact]
    public async Task SaveChanges_WithoutChanges_DoesNotThrow() {
        var act = async () => await _repository.SaveChanges();

        await act.Should().NotThrowAsync();
    }

    // ── HasActiveTransaction ────────────────────────────────────────────

    [Fact]
    public void HasActiveTransaction_NoTransaction_ReturnsFalse() {
        _repository.HasActiveTransaction().Should().BeFalse();
    }
}
